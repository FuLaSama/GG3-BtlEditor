namespace BtlCore.Front;

/// <summary>一份文档的提交、失败回滚和历史。单线程使用；嵌套操作加入同一事务。</summary>
public sealed class EditSession
{
    readonly BtlFrontDocument _document;
    readonly List<BtlFrontDocument> _history = new();
    readonly List<long> _sizes = new();
    readonly Dictionary<object, Action> _rollback = new(ReferenceEqualityComparer.Instance);
    readonly List<Action> _beforeCommit = new();
    bool _active;
    Exception _failure;
    int _index = -1;
    HistoryGroup _group;

    sealed class HistoryGroup(EditSession owner) : IDisposable
    {
        internal BtlFrontDocument Before;
        internal long BeforeBytes;
        public void Dispose() => owner.EndHistoryGroup(this);
    }

    public int HistoryCount => _history.Count;
    public bool IsEditing => _active;
    public bool IsGroupingHistory => _group != null;
    public int HistoryIndex => _index;
    public long HistoryBytes { get; private set; }
    public int CommitCount { get; private set; }
    public int SnapshotCount { get; private set; }
    public long Revision { get; private set; }
    public bool CanUndo => _index > 0;
    public bool CanRedo => _index >= 0 && _index < _history.Count - 1;
    public int HistoryLimit { get; init; } = 100;
    public long HistoryBudget { get; init; } = 128L * 1024 * 1024;

    internal EditSession(BtlFrontDocument document) => _document = document;

    public bool Execute(Action action, bool recordHistory = true)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_active)
        {
            try { action(); return false; }
            catch (Exception ex) { _failure ??= ex; throw; }
        }
        if (_document.HasUnresolvedData)
            throw new FrontEditException("文档包含未解析的源数据，当前会话仅允许查看。");
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        var before = BtlFrontJson.CloneDocument(_document);
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
        SnapshotCount++;
        _active = true;
        _failure = null;
        try
        {
            action();
            if (_failure != null) throw new FrontEditException("嵌套操作失败，整个事务已取消：" + _failure.Message);
            if (BtlFrontJson.ContentEquals(before, _document)) return false;
            foreach (var complete in _beforeCommit) complete();
            if (_group != null)
            {
                // 每次操作立即提交，仅延迟生成撤销帧。失败只回滚当前操作。
                if (_group.Before == null) { _group.Before = before; _group.BeforeBytes = beforeBytes; }
            }
            else if (recordHistory)
            {
                if (_history.Count == 0) Store(before, beforeBytes);
                RecordCurrent();
                CommitCount++;
            }
            Revision++;
            return true;
        }
        catch (Exception failure)
        {
            Restore(before);
            // 每个参与者只捕获一次，整个外层操作失败时恢复最初状态。
            List<Exception> recoveryErrors = null;
            foreach (var restore in _rollback.Values.Reverse())
            {
                try { restore(); }
                catch (Exception ex) { (recoveryErrors ??= new()).Add(ex); }
            }
            if (recoveryErrors != null)
                throw new AggregateException("事务失败，部分附加状态恢复失败。", new[] { failure }.Concat(recoveryErrors));
            throw;
        }
        finally
        {
            _active = false;
            _failure = null;
            _rollback.Clear();
            _beforeCommit.Clear();
        }
    }

    /// <summary>宿主可登记剪贴板、状态及运行时的恢复；核心不依赖脚本引擎。</summary>
    public void Enlist(object participant, Func<Action> capture)
    {
        if (!_active) throw new InvalidOperationException("只能在事务中登记回滚参与者。");
        if (!_rollback.ContainsKey(participant)) _rollback.Add(participant, capture());
    }

    /// <summary>在数据确有修改时、生成历史快照前记录附加来源。回调失败仍整体回滚。</summary>
    public void BeforeCommit(Action action)
    {
        if (!_active) throw new InvalidOperationException("只能在事务中登记提交回调。");
        _beforeCommit.Add(action ?? throw new ArgumentNullException(nameof(action)));
    }

    /// <summary>将连续交互中的成功操作合为一次撤销；不是跨事件的长事务。
    /// 调用方在鼠标释放、失去捕获或中断交互时 Dispose。无变化不生成历史。</summary>
    public IDisposable BeginHistoryGroup()
    {
        EnsureIdle();
        if (_group != null) throw new InvalidOperationException("历史分组不能嵌套。");
        if (_document.HasUnresolvedData) throw new FrontEditException("文档包含未解析的源数据，当前会话仅允许查看。");
        return _group = new HistoryGroup(this);
    }

    void EndHistoryGroup(HistoryGroup group)
    {
        if (group == null || !ReferenceEquals(_group, group)) return;
        EnsureIdle();
        if (group.Before != null && !BtlFrontJson.ContentEquals(group.Before, _document))
        {
            if (_history.Count == 0) Store(group.Before, group.BeforeBytes);
            RecordCurrent();
            CommitCount++;
        }
        else if (group.Before != null)
        {
            _document.FieldCatalogUses = group.Before.FieldCatalogUses;
            _document.PayloadCodecUses = group.Before.PayloadCodecUses;
        }
        _group = null;
    }

    /// <summary>prepared 必须是调用方交出所有权的独立快照，之后不得再修改它。</summary>
    public void ResetHistory(BtlFrontDocument prepared = null, long preparedBytes = 0)
    {
        EnsureIdle();
        if (_group != null) throw new InvalidOperationException("请先结束交互，再重置历史。");
        if (ReferenceEquals(prepared, _document)) throw new ArgumentException("初始历史不能直接引用当前可变文档。", nameof(prepared));
        if (prepared != null && (!ReferenceEquals(prepared.Context, _document.Context)
            || !BtlFrontJson.ContentEquals(prepared, _document)))
            throw new InvalidOperationException("初始历史与当前文档不一致。");
        _history.Clear(); _sizes.Clear(); HistoryBytes = 0; _index = -1;
        if (_document.HasUnresolvedData) return;
        if (prepared == null) StoreCurrent();
        else Store(prepared, preparedBytes);
    }

    /// <summary>迁移期间接收旧画布/注册表已完成的修改；新入口应使用 Execute。</summary>
    public bool RecordExternalChange()
    {
        EnsureIdle();
        if (_group != null) throw new InvalidOperationException("历史分组期间请通过 Execute 修改文档。");
        if (_document.HasUnresolvedData) return false;
        if (_history.Count == 0) { StoreCurrent(); return false; }
        if (_index >= 0 && BtlFrontJson.ContentEquals(_history[_index], _document)) return false;
        RecordCurrent(); Revision++; CommitCount++;
        return true;
    }

    public bool Undo()
    {
        EnsureIdle();
        EndHistoryGroup(_group);
        if (!CanUndo) return false;
        Restore(BtlFrontJson.CloneDocument(_history[--_index]));
        return true;
    }

    public bool Redo()
    {
        EnsureIdle();
        EndHistoryGroup(_group);
        if (!CanRedo) return false;
        Restore(BtlFrontJson.CloneDocument(_history[++_index]));
        return true;
    }

    void Restore(BtlFrontDocument snapshot)
    {
        _document.Root = snapshot.Root;
        _document.FormatVersion = snapshot.FormatVersion;
        _document.SourceIssues = snapshot.SourceIssues;
        _document.SourceSha256 = snapshot.SourceSha256;
        _document.FieldCatalogUses = snapshot.FieldCatalogUses;
        _document.PayloadCodecUses = snapshot.PayloadCodecUses;
        Revision++;
    }

    void RecordCurrent()
    {
        if (_index >= 0 && BtlFrontJson.ContentEquals(_history[_index], _document)) return;
        for (int i = _index + 1; i < _sizes.Count; i++) HistoryBytes -= _sizes[i];
        _sizes.RemoveRange(_index + 1, _sizes.Count - _index - 1);
        _history.RemoveRange(_index + 1, _history.Count - _index - 1);
        StoreCurrent();
        while (_history.Count > Math.Max(2, HistoryLimit) || _history.Count > 2 && HistoryBytes > HistoryBudget)
        {
            HistoryBytes -= _sizes[0]; _sizes.RemoveAt(0); _history.RemoveAt(0); _index--;
        }
    }

    void StoreCurrent()
    {
        long start = GC.GetAllocatedBytesForCurrentThread();
        var snapshot = BtlFrontJson.CloneDocument(_document);
        Store(snapshot, GC.GetAllocatedBytesForCurrentThread() - start);
    }

    void Store(BtlFrontDocument snapshot, long bytes)
    {
        _history.Add(snapshot); _sizes.Add(bytes); HistoryBytes += bytes;
        _index = _history.Count - 1;
    }

    void EnsureIdle()
    {
        if (_active) throw new InvalidOperationException("事务执行期间不能操作历史。");
    }
}
