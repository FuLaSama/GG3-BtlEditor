using BtlCore.Scripting;
using Xunit;

namespace BtlCore.Front.Tests;

public class EditSessionTests
{
    static BtlFrontDocument Blank() => new() { Root = BtlFrontJson.NewTable() };

    [Fact]
    public void Gesture_commits_immediately_but_produces_one_undo_frame()
    {
        var doc = Blank();
        var host = new ScriptHost(); host.Load(doc);
        host.Execute("editor.action('set', function(ctx) editor.data.set('/0','u16',ctx.value) end)", "set.lua");
        using (doc.Editing.BeginHistoryGroup())
        {
            host.Run("set", new() { Value = 1 });
            Assert.Equal(1, Convert.ToInt32(((BtlScalar)doc.Root.F[0]).V));
            Assert.Equal(0, doc.Editing.HistoryCount);
            host.Run("set", new() { Value = 2 });
            Assert.Throws<InvalidOperationException>(() => doc.Editing.Execute(() =>
            {
                FrontEdit.SetScalar(doc.Root, 0, "u16", (ushort)99);
                throw new InvalidOperationException("failed movement");
            }));
            Assert.Equal(2, Convert.ToInt32(((BtlScalar)doc.Root.F[0]).V));
        }
        Assert.Equal(2, doc.Editing.HistoryCount);
        Assert.Equal(1, doc.Editing.CommitCount);
        Assert.True(doc.Editing.Undo()); Assert.Empty(doc.Root.F);
        Assert.True(doc.Editing.Redo()); Assert.Equal(2, Convert.ToInt32(((BtlScalar)doc.Root.F[0]).V));
    }

    [Fact]
    public void Empty_and_reverted_gestures_preserve_redo_and_do_not_add_history()
    {
        var doc = Blank();
        doc.Editing.Execute(() => FrontEdit.SetScalar(doc.Root, 0, "u8", (byte)1));
        doc.Editing.Undo();
        using (doc.Editing.BeginHistoryGroup()) { }
        using (doc.Editing.BeginHistoryGroup())
        {
            doc.Editing.Execute(() => FrontEdit.SetScalar(doc.Root, 0, "u8", (byte)2));
            doc.Editing.Execute(() => FrontEdit.SetField(doc.Root, 0, null));
        }
        Assert.Equal(2, doc.Editing.HistoryCount);
        Assert.Equal(1, doc.Editing.CommitCount);
        Assert.True(doc.Editing.CanRedo);
        Assert.True(doc.Editing.Redo());
    }

    [Fact]
    public void Undo_finishes_an_interrupted_gesture_and_old_scope_cannot_close_a_new_one()
    {
        var doc = Blank();
        var first = doc.Editing.BeginHistoryGroup();
        doc.Editing.Execute(() => FrontEdit.SetScalar(doc.Root, 0, "u8", (byte)1));
        Assert.True(doc.Editing.Undo()); Assert.Empty(doc.Root.F);
        using (doc.Editing.BeginHistoryGroup())
        {
            first.Dispose();
            Assert.True(doc.Editing.IsGroupingHistory);
            Assert.Throws<InvalidOperationException>(() => doc.Editing.BeginHistoryGroup());
            Assert.Throws<InvalidOperationException>(() => doc.Editing.ResetHistory());
            doc.Editing.Execute(() => FrontEdit.SetScalar(doc.Root, 0, "u8", (byte)2));
        }
        Assert.False(doc.Editing.CanRedo);
        Assert.Equal(2, doc.Editing.HistoryCount);
        Assert.True(doc.Editing.Undo()); Assert.Empty(doc.Root.F);
    }

    [Fact]
    public void Native_and_script_operations_share_one_undo_redo_history()
    {
        var doc = Blank();
        doc.Editing.ResetHistory();
        var host = new ScriptHost(); host.Load(doc);
        host.Execute("editor.action('set', function(ctx) editor.data.set('/0','u16',ctx.value) end)", "set.lua");
        doc.Editing.Execute(() => FrontEdit.SetScalar(doc.Root, 0, "u16", (ushort)1));
        host.Run("set", new() { Value = 2 });
        Assert.Equal(3, doc.Editing.HistoryCount);
        Assert.True(doc.Editing.Undo()); Assert.Equal(1, Convert.ToInt32(((BtlScalar)doc.Root.F[0]).V));
        Assert.True(doc.Editing.Undo()); Assert.Empty(doc.Root.F);
        Assert.True(doc.Editing.Redo()); Assert.Equal(1, Convert.ToInt32(((BtlScalar)doc.Root.F[0]).V));
        Assert.True(doc.Editing.Redo()); Assert.Equal(2, Convert.ToInt32(((BtlScalar)doc.Root.F[0]).V));
        host.Run("set", new() { Value = 2 });
        Assert.Equal(3, doc.Editing.HistoryCount);
        Assert.Equal(2, host.UndoFrames);
        doc.Editing.Undo();
        host.Run("set", new() { Value = 3 });
        Assert.False(doc.Editing.CanRedo);
        Assert.Equal(3, doc.Editing.HistoryCount);
    }

    [Fact]
    public void Nested_native_and_script_changes_use_one_snapshot_and_commit()
    {
        var doc = Blank();
        var host = new ScriptHost(); host.Load(doc);
        host.Execute("editor.action('set', function(ctx) editor.data.set('/0','u16',ctx.value) end)", "set.lua");
        doc.Editing.Execute(() =>
        {
            host.Run("set", new() { Value = 1 });
            host.Run("set", new() { Value = 2 });
            FrontEdit.SetScalar(doc.Root, 33, "u8", (byte)7);
        });
        Assert.Equal(1, host.SnapshotCount);
        Assert.Equal(1, host.UndoFrames);
        Assert.Equal(2, doc.Editing.HistoryCount);
        doc.Editing.Undo(); Assert.Empty(doc.Root.F);
    }

    [Fact]
    public void Failed_outer_transaction_restores_document_script_state_and_runtime()
    {
        var doc = Blank();
        var host = new ScriptHost(); host.Load(doc);
        host.Execute("""
            local counter=0
            editor.action('set',function()
                counter=counter+1
                editor.state.set('count',counter)
                editor.data.set('/0','u16',counter)
            end)
            editor.action('read',{get=function() return editor.state.get('count') end})
            """, "state.lua");
        Assert.Throws<InvalidOperationException>(() => doc.Editing.Execute(() =>
        {
            host.Run("set", new()); host.Run("set", new());
            throw new InvalidOperationException("native failure");
        }));
        Assert.Empty(doc.Root.F);
        Assert.Null(host.CallGet("read"));
        Assert.Equal(0, host.UndoFrames);
        host.Run("set", new());
        Assert.Equal(1, Convert.ToInt32(((BtlScalar)doc.Root.F[0]).V));
    }

    [Fact]
    public void Caught_nested_failure_still_cancels_the_entire_transaction()
    {
        var doc = Blank();
        Assert.Throws<FrontEditException>(() => doc.Editing.Execute(() =>
        {
            try
            {
                doc.Editing.Execute(() =>
                {
                    FrontEdit.SetScalar(doc.Root, 0, "u16", (ushort)3);
                    throw new InvalidOperationException("nested failure");
                });
            }
            catch (InvalidOperationException) { }
            FrontEdit.SetScalar(doc.Root, 1, "u8", (byte)4);
        }));
        Assert.Empty(doc.Root.F);
        Assert.Equal(0, doc.Editing.HistoryCount);
    }
}
