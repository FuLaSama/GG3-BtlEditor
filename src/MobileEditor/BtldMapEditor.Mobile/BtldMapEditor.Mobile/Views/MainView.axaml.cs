using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using BtldMapEditor;

namespace BtldMapEditor.Mobile.Views
{
    public partial class MainView : UserControl
    {
        private StageModel _stage;
        private int _selectedCellIdx = -1;
        private bool _isUpdatingUi = false;
        private List<string> _allBtlPaths = new List<string>();
        private List<string> _filteredBtlPaths = new List<string>();
        private string _loadedFilePath;

        // Clipboard variables
        private ushort? _copiedTerrain;
        private TileAttrModel _copiedAttr;
        private TileAttrModel _copiedAttrA2;
        private TileAttrModel _copiedAttrA3;
        private AIAgentModel _copiedUnit;
        private TriggerEventModel _copiedTriggerBldg;
        private TriggerEventModel _copiedTriggerFort;

        public MainView()
        {
            InitializeComponent();
            RegisterEvents();
            LoadDefaultStageIfAvailable();
        }

        private void RegisterEvents()
        {
            // 按钮事件
            btnCopy.Click += OnCopyClicked;
            btnPaste.Click += OnPasteClicked;
            btnOpen.Click += (s, e) => OpenSystemFilePickerAsync();
            btnSave.Click += (s, e) => OpenSaveModalDialog();

            // 弹出层事件
            btnCloseBtlList.Click += (s, e) => overlayBtlList.IsVisible = false;
            btnCancelBtlLoad.Click += (s, e) => overlayBtlList.IsVisible = false;
            btnConfirmBtlLoad.Click += (s, e) => ConfirmBtlLoad();
            
            lstBtlFiles.DoubleTapped += (s, e) => ConfirmBtlLoad();

            // 保存弹出层事件
            btnCloseSaveMap.Click += (s, e) => overlaySaveMap.IsVisible = false;
            btnCancelSaveMap.Click += (s, e) => overlaySaveMap.IsVisible = false;
            btnConfirmSaveMap.Click += (s, e) => ConfirmSaveMap();

            txtBtlSearch.PropertyChanged += (s, e) =>
            {
                if (e.Property.Name == nameof(TextBox.Text))
                {
                    FilterBtlList();
                }
            };

            // 地图选择与画笔事件
            mapCanvas.CellSelected += OnCellSelected;
            mapCanvas.PaintCellRequested += OnPaintCellRequested;

            // 渲染模式
            cbRenderMode.SelectionChanged += (s, e) =>
            {
                if (mapCanvas != null && cbRenderMode != null)
                {
                    mapCanvas.RenderMode = (MapRenderMode)cbRenderMode.SelectedIndex;
                }
            };

            // 画笔模式
            chkBrushMode.IsCheckedChanged += (s, e) =>
            {
                bool brush = chkBrushMode.IsChecked ?? false;
                if (mapCanvas != null)
                {
                    mapCanvas.BrushMode = brush;
                }
            };

            // 属性面板数据变化事件
            nudBaseTerrain.ValueChanged += (s, e) => OnTerrainPropertyChanged();
            nudVariation.ValueChanged += (s, e) => OnTerrainPropertyChanged();
            chkPlayableFlag.IsCheckedChanged += (s, e) => OnTerrainPropertyChanged();
            chkOceanFlag.IsCheckedChanged += (s, e) => OnTerrainPropertyChanged();

            chkSlot1.IsCheckedChanged += (s, e) => OnTerrainPropertyChanged();
            nudSlot1Id.ValueChanged += (s, e) => OnTerrainPropertyChanged();
            nudSlot1Val.ValueChanged += (s, e) => OnTerrainPropertyChanged();
            nudSlot1Dx.ValueChanged += (s, e) => OnTerrainPropertyChanged();
            nudSlot1Dy.ValueChanged += (s, e) => OnTerrainPropertyChanged();

            chkSlot2.IsCheckedChanged += (s, e) => OnTerrainPropertyChanged();
            nudSlot2Id.ValueChanged += (s, e) => OnTerrainPropertyChanged();
            nudSlot2Val.ValueChanged += (s, e) => OnTerrainPropertyChanged();
            nudSlot2Dx.ValueChanged += (s, e) => OnTerrainPropertyChanged();
            nudSlot2Dy.ValueChanged += (s, e) => OnTerrainPropertyChanged();

            chkSlot3.IsCheckedChanged += (s, e) => OnTerrainPropertyChanged();
            nudSlot3Id.ValueChanged += (s, e) => OnTerrainPropertyChanged();
            nudSlot3Val.ValueChanged += (s, e) => OnTerrainPropertyChanged();
            nudSlot3Dx.ValueChanged += (s, e) => OnTerrainPropertyChanged();
            nudSlot3Dy.ValueChanged += (s, e) => OnTerrainPropertyChanged();

            // 部队事件
            chkHasUnit.IsCheckedChanged += (s, e) => OnUnitPropertyChanged();
            btnDeleteUnit.Click += (s, e) => { chkHasUnit.IsChecked = false; };
            btnDeleteBuilding.Click += (s, e) => { chkHasBuilding.IsChecked = false; };
            btnDeleteFort.Click += (s, e) => { chkHasFort.IsChecked = !(chkHasFort.IsChecked ?? false); };
            nudUnitId.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitFaction.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitHp.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitMaxHp.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitStack.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitAiTarget.ValueChanged += (s, e) => OnUnitPropertyChanged();

            nudUnitMobility.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitLevel.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitDirection.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitVal8.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitVal9.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitAgentId.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitPlayMode.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitAiTarget.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitFactionExtra.ValueChanged += (s, e) => OnUnitPropertyChanged();

            chkUnitBehavior.IsCheckedChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitBehaviorId.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitBehaviorRadius.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitBehaviorCenter.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitBehaviorField0.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitBehaviorField2.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitBehaviorField4.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitBehaviorField6.ValueChanged += (s, e) => OnUnitPropertyChanged();

            chkUnitGeneral.IsCheckedChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitGeneralId.ValueChanged += (s, e) => OnUnitPropertyChanged();
            chkUnitGeneralActive.IsCheckedChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitGeneralParam2.ValueChanged += (s, e) => OnUnitPropertyChanged();
                        
            chkUnitExArmy.IsCheckedChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitExArmyId.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitExArmyHp.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitExArmyMaxHp.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitExArmyField1.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitExArmyField4.ValueChanged += (s, e) => OnUnitPropertyChanged();
            nudUnitExArmyField5.ValueChanged += (s, e) => OnUnitPropertyChanged();

            // 建筑事件
            chkHasBuilding.IsCheckedChanged += (s, e) => OnBuildingPropertyChanged();
            nudBuildingId.ValueChanged += (s, e) => OnBuildingPropertyChanged();
            nudBldgOwner.ValueChanged += (s, e) => OnBuildingPropertyChanged();
            nudBldgDx.ValueChanged += (s, e) => OnBuildingPropertyChanged();
            nudBldgDy.ValueChanged += (s, e) => OnBuildingPropertyChanged();
            nudBldgFlag.ValueChanged += (s, e) => OnBuildingPropertyChanged();
            nudBldgExtraFlag.ValueChanged += (s, e) => OnBuildingPropertyChanged();
            nudBldgField6.ValueChanged += (s, e) => OnBuildingPropertyChanged();

            chkHasFort.IsCheckedChanged += (s, e) => OnBuildingPropertyChanged();
            nudFortType.ValueChanged += (s, e) => OnBuildingPropertyChanged();
            nudFortField1.ValueChanged += (s, e) => OnBuildingPropertyChanged();
            nudFortField3.ValueChanged += (s, e) => OnBuildingPropertyChanged();

            // 势力事件
            lstFactions.SelectionChanged += OnFactionSelectionChanged;
            btnAddFaction.Click += OnAddFactionClicked;
            btnDeleteFaction.Click += OnDeleteFactionClicked;
            btnFactionUp.Click += OnMoveFactionUpClicked;
            btnFactionDown.Click += OnMoveFactionDownClicked;

            nudFactionId.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionCamp.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionCountry.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionIsAI.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionGeneralLimit.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionAlign1.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionAlign2.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionGold.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionTech.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionIncomeMod.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionDamageMod.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionHpMod.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionColorR.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionColorG.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionColorB.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionColorA.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionGeneralFlag.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionConfigId.ValueChanged += (s, e) => OnFactionPropertyChanged();
            nudFactionConfigRef.ValueChanged += (s, e) => OnFactionPropertyChanged();

            // 全局元数据与目标事件
            nudStageNum.ValueChanged += (s, e) => OnGlobalMetadataChanged();
            nudVersion.ValueChanged += (s, e) => OnGlobalMetadataChanged();
            nudTag.ValueChanged += (s, e) => OnGlobalMetadataChanged();

            lstStageTargets.SelectionChanged += OnTargetSelectionChanged;
            btnAddTarget.Click += OnAddTargetClicked;
            btnSaveTarget.Click += OnSaveTargetClicked;
            btnDeleteTarget.Click += OnDeleteTargetClicked;

            // 增援事件
            lstReinforces.SelectionChanged += OnReinforceSelectionChanged;
            btnAddReinforce.Click += OnAddReinforceClicked;
            btnDeleteReinforce.Click += OnDeleteReinforceClicked;
            btnSaveReinforce.Click += (s, e) => OnReinforcePropertyChanged();

            nudRpCellIdx.ValueChanged += (s, e) => OnReinforcePropertyChanged();
            nudRpFactionId.ValueChanged += (s, e) => OnReinforcePropertyChanged();
            chkRpIsKey.IsCheckedChanged += (s, e) => OnReinforcePropertyChanged();
            nudRpFlag.ValueChanged += (s, e) => OnReinforcePropertyChanged();

            // 地图大小与可游玩区域调整
            btnResizeMap.Click += OnResizeMapClicked;
            nudLeftMargin.ValueChanged += (s, e) => OnPlayableBoundsChanged();
            nudTopMargin.ValueChanged += (s, e) => OnPlayableBoundsChanged();
            nudPlayWidth.ValueChanged += (s, e) => OnPlayableBoundsChanged();
            nudPlayHeight.ValueChanged += (s, e) => OnPlayableBoundsChanged();
        }

        private void OpenBtlListOverlay()
        {
            txtBtlSearch.Text = "";
            RefreshBtlFileList();
            overlayBtlList.IsVisible = true;
        }

        private void RefreshBtlFileList()
        {
            _allBtlPaths.Clear();
            string baseDir = GameSettings.ExternalDataDir;
            if (string.IsNullOrEmpty(baseDir)) return;

            var searchPaths = new[]
            {
                Path.Combine(baseDir, "BTL"),
                Path.Combine(baseDir, "战役相关文件", "BTL"),
                baseDir
            };

            foreach (var path in searchPaths)
            {
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path, "*.*")
                        .Where(file => file.EndsWith(".btl", StringComparison.OrdinalIgnoreCase) || 
                                       file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    foreach (var file in files)
                    {
                        if (!_allBtlPaths.Contains(file))
                        {
                            _allBtlPaths.Add(file);
                        }
                    }
                }
            }

            FilterBtlList();
        }

        private void FilterBtlList()
        {
            string query = txtBtlSearch.Text ?? "";
            _filteredBtlPaths = _allBtlPaths
                .Where(p => string.IsNullOrEmpty(query) || Path.GetFileName(p).Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                .ToList();

            lstBtlFiles.ItemsSource = _filteredBtlPaths.Select(Path.GetFileName).ToList();

            if (_filteredBtlPaths.Count > 0)
            {
                lstBtlFiles.SelectedIndex = 0;
            }
        }

        private async void OpenSystemFilePickerAsync()
        {
            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "选择要打开的关卡文件",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        FilePickerFileTypes.All,
                        new FilePickerFileType("BTL / JSON 关卡文件 (*.btl, *.json)")
                        {
                            Patterns = new[] { "*.btl", "*.json" },
                            MimeTypes = new[] { "application/octet-stream", "application/json", "*/*" }
                        },
                        new FilePickerFileType("BTL 关卡文件 (*.btl)")
                        {
                            Patterns = new[] { "*.btl" },
                            MimeTypes = new[] { "application/octet-stream", "*/*" }
                        },
                        new FilePickerFileType("JSON 文件 (*.json)")
                        {
                            Patterns = new[] { "*.json" },
                            MimeTypes = new[] { "application/json", "*/*" }
                        }
                    }
                });

                if (files != null && files.Count > 0)
                {
                    var item = files[0];
                    string filePath = item.Path.LocalPath;
                    if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                    {
                        LoadBtlFile(filePath);
                    }
                    else
                    {
                        using (var stream = await item.OpenReadAsync())
                        {
                            string tempFile = Path.Combine(Path.GetTempPath(), item.Name);
                            using (var fileStream = File.Create(tempFile))
                            {
                                await stream.CopyToAsync(fileStream);
                            }
                            LoadBtlFile(tempFile);
                            _loadedFilePath = item.Name;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                txtMapSize.Text = "打开文件失败";
                Console.WriteLine($"[打开文件失败] {ex.Message}");
            }
        }

        private void OpenSaveModalDialog()
        {
            if (_stage == null) return;
            string currentName = !string.IsNullOrEmpty(_loadedFilePath)
                ? Path.GetFileNameWithoutExtension(_loadedFilePath)
                : "stage_01";

            txtSaveFileName.Text = currentName;

            if (!string.IsNullOrEmpty(_loadedFilePath))
            {
                string lower = _loadedFilePath.ToLower();
                if (lower.EndsWith(".btld.json")) cbSaveFormat.SelectedIndex = 1;
                else if (lower.EndsWith(".json")) cbSaveFormat.SelectedIndex = 2;
                else cbSaveFormat.SelectedIndex = 0;
            }
            else
            {
                cbSaveFormat.SelectedIndex = 0;
            }

            overlaySaveMap.IsVisible = true;
        }

        private async void ConfirmSaveMap()
        {
            if (_stage == null) return;
            string rawName = txtSaveFileName.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(rawName))
            {
                txtMapSize.Text = "错误：文件名不能为空！";
                return;
            }

            overlaySaveMap.IsVisible = false;

            int selectedFormat = cbSaveFormat.SelectedIndex;
            string ext = ".btl";
            if (selectedFormat == 1) ext = ".btld.json";
            else if (selectedFormat == 2) ext = ".json";

            string baseName = rawName;
            if (baseName.EndsWith(".btld.json", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - 10);
            else if (baseName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - 5);
            else if (baseName.EndsWith(".btl", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - 4);

            string fullFileName = baseName + ext;

            await SaveStageWithPickerAsync(fullFileName, selectedFormat);
        }

        private async System.Threading.Tasks.Task SaveStageWithPickerAsync(string targetFileName, int formatIndex)
        {
            try
            {
                if (_stage == null) return;
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                FilePickerFileType fileTypeChoice;
                if (formatIndex == 1)
                {
                    fileTypeChoice = new FilePickerFileType("BTLD JSON 文件 (*.btld.json)")
                    {
                        Patterns = new[] { "*.btld.json" },
                        MimeTypes = new[] { "application/json", "text/plain", "*/*" }
                    };
                }
                else if (formatIndex == 2)
                {
                    fileTypeChoice = new FilePickerFileType("JSON 关卡文件 (*.json)")
                    {
                        Patterns = new[] { "*.json" },
                        MimeTypes = new[] { "application/json", "text/plain", "*/*" }
                    };
                }
                else
                {
                    fileTypeChoice = new FilePickerFileType("BTL 关卡文件 (*.btl)")
                    {
                        Patterns = new[] { "*.btl" },
                        MimeTypes = new[] { "application/octet-stream", "*/*" }
                    };
                }

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "选择保存位置",
                    SuggestedFileName = targetFileName,
                    DefaultExtension = formatIndex == 0 ? "btl" : "json",
                    ShowOverwritePrompt = true,
                    FileTypeChoices = new[] { fileTypeChoice, FilePickerFileTypes.All }
                });

                if (file != null)
                {
                    string saveName = file.Name;
                    if (string.IsNullOrEmpty(saveName)) saveName = targetFileName;

                    string lowerSaveName = saveName.ToLower();
                    if (formatIndex == 1 && !lowerSaveName.EndsWith(".btld.json")) saveName += ".btld.json";
                    else if (formatIndex == 2 && !lowerSaveName.EndsWith(".json")) saveName += ".json";
                    else if (formatIndex == 0 && !lowerSaveName.EndsWith(".btl")) saveName += ".btl";

                    string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "_" + saveName);

                    // 1. Write stage to temp file with chosen format
                    SaveStageToPathWithFormat(tempFile, formatIndex);

                    // 2. Stream temp file into StorageProvider output stream
                    using (var outStream = await file.OpenWriteAsync())
                    {
                        try { outStream.SetLength(0); } catch { }
                        using (var inStream = File.OpenRead(tempFile))
                        {
                            await inStream.CopyToAsync(outStream);
                            await outStream.FlushAsync();
                        }
                    }

                    // 3. Try direct local copy if LocalPath is accessible
                    try
                    {
                        string localPath = file.Path.LocalPath;
                        if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                        {
                            File.Copy(tempFile, localPath, overwrite: true);
                        }
                    }
                    catch { }

                    try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }

                    _loadedFilePath = saveName;
                    txtMapSize.Text = $"地图: {saveName} ({_stage.MapTerrain?.Size?.Width}x{_stage.MapTerrain?.Size?.Height}) (已保存)";
                }
            }
            catch (Exception ex)
            {
                txtMapSize.Text = "保存文件失败";
                Console.WriteLine($"[保存文件失败] {ex.Message}");
            }
        }

                private void SyncCellsToStage()
        {
            if (_stage == null || mapCanvas?.Cells == null || mapCanvas.Cells.Count == 0) return;
            if (_stage.MapTerrain == null) _stage.MapTerrain = new MapTerrainModel();

            var newTiles = new List<ushort>();
            var newAttributes = new List<TileAttrModel>();

            foreach (var cell in mapCanvas.Cells)
            {
                ushort terrain = cell.Terrain;
                byte v6 = (byte)(terrain >> 8);

                newTiles.Add(terrain);

                if ((v6 & 4) != 0)
                {
                    newAttributes.Add(cell.Attr ?? new TileAttrModel { Byte0 = 0, Byte1 = 0, Byte2 = 0, Byte3 = 0 });
                }

                if ((v6 & 8) != 0)
                {
                    newAttributes.Add(cell.AttrA2 ?? new TileAttrModel { Byte0 = 0, Byte1 = 0, Byte2 = 0, Byte3 = 0 });
                }

                if ((v6 & 0x10) != 0)
                {
                    newAttributes.Add(cell.AttrA3 ?? new TileAttrModel { Byte0 = 0, Byte1 = 0, Byte2 = 0, Byte3 = 0 });
                }
            }

            _stage.MapTerrain.Tiles = newTiles;
            _stage.MapTerrain.Attributes = newAttributes;
        }

        private void SaveStageToPathWithFormat(string targetPath, int formatIndex)
        {
            if (_stage == null || string.IsNullOrEmpty(targetPath)) return;
            try
            {
                SyncCellsToStage();

                string targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                if (formatIndex == 1) // BTLD JSON
                {
                    string json = System.Text.Json.JsonSerializer.Serialize(_stage, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    });
                    string customJson = BtlToolchain.Toolchain.LogicalToBtldJson(json);
                    File.WriteAllText(targetPath, customJson);
                }
                else if (formatIndex == 2) // Clean JSON
                {
                    string json = System.Text.Json.JsonSerializer.Serialize(_stage, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    });
                    File.WriteAllText(targetPath, json);
                }
                else // BTL Binary
                {
                    EditorBridge.SaveToBtl(_stage, targetPath);
                }
            }
            catch (Exception ex)
            {
                txtMapSize.Text = "保存失败";
                Console.WriteLine($"[保存失败] {ex.Message}");
            }
        }

        private void SaveStageToPath(string targetPath)
        {
            SaveStageToPathWithFormat(targetPath, targetPath.ToLower().EndsWith(".json") ? 2 : 0);
        }
        private void ConfirmBtlLoad()
        {
            int selectedIdx = lstBtlFiles.SelectedIndex;
            if (selectedIdx >= 0 && selectedIdx < _filteredBtlPaths.Count)
            {
                string selectedPath = _filteredBtlPaths[selectedIdx];
                LoadBtlFile(selectedPath);
                overlayBtlList.IsVisible = false;
            }
        }

        private void LoadBtlFile(string path)
        {
            try
            {
                GameSettings.LoadAllSettings();

                string ext = Path.GetExtension(path).ToLower();
                if (ext == ".json")
                {
                    string jsonStr = File.ReadAllText(path);
                    string schemaJson = jsonStr.Contains("table_") || jsonStr.Contains("field_") 
                        ? BtlToolchain.Toolchain.BtldToLogicalJson(jsonStr) 
                        : jsonStr;
                    _stage = System.Text.Json.JsonSerializer.Deserialize<StageModel>(schemaJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                else
                {
                    _stage = EditorBridge.LoadFromBtl(path);
                }

                _loadedFilePath = path;
                _selectedCellIdx = -1;

                welcomeOverlay.IsVisible = false;
                mapCanvas.Stage = _stage;
                txtMapSize.Text = $"地图: {Path.GetFileName(path)} ({_stage.MapTerrain?.Size?.Width}x{_stage.MapTerrain?.Size?.Height})";



                // Faction and Reinforcement lists
                RefreshFactionList();
                RefreshReinforceList();

                _isUpdatingUi = true;
                RefreshTargetsList();

                // Global properties
                if (_stage.StageMetadata != null)
                {
                    nudStageNum.Value = _stage.StageMetadata.StageNum;
                }
                nudVersion.Value = _stage.Version;
                if (_stage.TriggerInfo != null)
                {
                    nudTag.Value = _stage.TriggerInfo.Tag;
                }

                if (_stage.MapTerrain?.Size != null)
                {
                    nudMapW.Value = _stage.MapTerrain.Size.Width;
                    nudMapH.Value = _stage.MapTerrain.Size.Height;
                    nudLeftMargin.Value = _stage.MapTerrain.Size.LeftMargin;
                    nudTopMargin.Value = _stage.MapTerrain.Size.TopMargin;
                    nudPlayWidth.Value = _stage.MapTerrain.Size.PlayableWidth;
                    nudPlayHeight.Value = _stage.MapTerrain.Size.PlayableHeight;
                }
                _isUpdatingUi = false;

                UpdateSidebarUi();
            }
            catch (Exception ex)
            {
                txtMapSize.Text = "载入文件出错！";
                Console.WriteLine($"[载入出错] {ex.Message}");
            }
        }

        private void OnCopyClicked(object sender, RoutedEventArgs e)
        {
            if (_selectedCellIdx < 0 || mapCanvas.Cells == null || _selectedCellIdx >= mapCanvas.Cells.Count) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];

            int activeTab = tabSidebar.SelectedIndex;
            if (activeTab == 0) // 地形
            {
                _copiedTerrain = cell.Terrain;
                _copiedAttr = CloneJson(cell.Attr);
                _copiedAttrA2 = CloneJson(cell.AttrA2);
                _copiedAttrA3 = CloneJson(cell.AttrA3);
                txtMapSize.Text = $"已复制地块 #{_selectedCellIdx} 的地形和插槽";
            }
            else if (activeTab == 1) // 部队
            {
                if (cell.Unit != null)
                {
                    _copiedUnit = CloneJson(cell.Unit);
                    txtMapSize.Text = $"已复制地块 #{_selectedCellIdx} 的部队配置";
                }
                else
                {
                    _copiedUnit = null;
                    txtMapSize.Text = $"地块 #{_selectedCellIdx} 无部队可复制";
                }
            }
            else if (activeTab == 2) // 建筑
            {
                _copiedTriggerBldg = CloneJson(cell.TriggerBldg);
                _copiedTriggerFort = CloneJson(cell.TriggerFort);
                txtMapSize.Text = $"已复制地块 #{_selectedCellIdx} 的建筑和要塞";
            }
            else
            {
                txtMapSize.Text = "当前选中的栏目不支持复制地块数据";
            }
        }

        private void OnPasteClicked(object sender, RoutedEventArgs e)
        {
            if (_selectedCellIdx < 0 || mapCanvas.Cells == null || _selectedCellIdx >= mapCanvas.Cells.Count || _stage == null) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];

            int activeTab = tabSidebar.SelectedIndex;
            if (activeTab == 0) // 地形
            {
                if (_copiedTerrain.HasValue)
                {
                    cell.Terrain = _copiedTerrain.Value;
                    cell.Attr = CloneJson(_copiedAttr);
                    cell.AttrA2 = CloneJson(_copiedAttrA2);
                    cell.AttrA3 = CloneJson(_copiedAttrA3);

                    // Sync to stage tiles list
                    _stage.MapTerrain.Tiles[_selectedCellIdx] = cell.Terrain;
                }
                else
                {
                    txtMapSize.Text = "剪贴板中无地形数据";
                    return;
                }
            }
            else if (activeTab == 1) // 部队
            {
                if (cell.Unit != null)
                {
                    _stage.AIInfo?.Agents?.Remove(cell.Unit);
                    cell.Unit = null;
                }

                if (_copiedUnit != null)
                {
                    cell.Unit = CloneJson(_copiedUnit);
                    cell.Unit.AgentInfo.CellIdx = (ushort)_selectedCellIdx;
                    cell.Unit.AgentInfo.AgentId = GetNextUniqueAgentId();

                    if (_stage.AIInfo == null) _stage.AIInfo = new AIInfoModel();
                    if (_stage.AIInfo.Agents == null) _stage.AIInfo.Agents = new List<AIAgentModel>();
                    _stage.AIInfo.Agents.Add(cell.Unit);
                }
            }
            else if (activeTab == 2) // 建筑
            {
                if (cell.TriggerBldg != null)
                {
                    _stage.TriggerInfo?.Events?.Remove(cell.TriggerBldg);
                    cell.TriggerBldg = null;
                }
                if (cell.TriggerFort != null)
                {
                    _stage.TriggerInfo?.Events?.Remove(cell.TriggerFort);
                    cell.TriggerFort = null;
                }

                if (_copiedTriggerBldg != null)
                {
                    cell.TriggerBldg = CloneJson(_copiedTriggerBldg);
                    cell.TriggerBldg.TileIndex = (ushort)_selectedCellIdx;

                    if (_stage.TriggerInfo == null) _stage.TriggerInfo = new TriggerInfoModel();
                    if (_stage.TriggerInfo.Events == null) _stage.TriggerInfo.Events = new List<TriggerEventModel>();
                    _stage.TriggerInfo.Events.Add(cell.TriggerBldg);
                }

                if (_copiedTriggerFort != null)
                {
                    cell.TriggerFort = CloneJson(_copiedTriggerFort);
                    cell.TriggerFort.TileIndex = (ushort)_selectedCellIdx;

                    if (_stage.TriggerInfo == null) _stage.TriggerInfo = new TriggerInfoModel();
                    if (_stage.TriggerInfo.Events == null) _stage.TriggerInfo.Events = new List<TriggerEventModel>();
                    _stage.TriggerInfo.Events.Add(cell.TriggerFort);
                }
            }
            else
            {
                txtMapSize.Text = "当前选中的栏目不支持粘贴地块数据";
                return;
            }

            mapCanvas.InvalidateCellCache(_selectedCellIdx);
            UpdateSidebarUi();
            mapCanvas.InvalidateVisual();
            txtMapSize.Text = $"已粘贴属性至地块 #{_selectedCellIdx}";
        }

        private static T CloneJson<T>(T source)
        {
            if (source == null) return default;
            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(source);
                return System.Text.Json.JsonSerializer.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JSON克隆出错] {ex.Message}");
                return default;
            }
        }

        private ushort GetNextUniqueAgentId()
        {
            ushort maxId = 0;
            if (mapCanvas.Cells != null)
            {
                foreach (var cell in mapCanvas.Cells)
                {
                    if (cell.Unit?.AgentInfo?.AgentId != null && cell.Unit.AgentInfo.AgentId.Value > maxId)
                    {
                        maxId = cell.Unit.AgentInfo.AgentId.Value;
                    }
                }
            }
            if (_stage?.AIInfo?.Agents != null)
            {
                foreach (var agent in _stage.AIInfo.Agents)
                {
                    if (agent.AgentInfo?.AgentId != null && agent.AgentInfo.AgentId.Value > maxId)
                    {
                        maxId = agent.AgentInfo.AgentId.Value;
                    }
                }
            }
            return (ushort)(maxId + 1);
        }

        private void LoadDefaultStageIfAvailable()
        {
            RefreshBtlFileList();
            _stage = null;
            _loadedFilePath = null;
            _selectedCellIdx = -1;
            mapCanvas.Stage = null;
            welcomeOverlay.IsVisible = true;
            txtMapSize.Text = "地图: 未加载关卡，请点击左上角“打开”选择关卡文件";
            UpdateSidebarUi();
        }

        private void OnCellSelected(int cellIndex)
        {
            _selectedCellIdx = cellIndex;
            UpdateSidebarUi();
        }

        private void OnPaintCellRequested(int cellIndex)
        {
            if (_stage == null || cellIndex < 0 || cellIndex >= mapCanvas.Cells.Count) return;
            var cell = mapCanvas.Cells[cellIndex];

            int activeTab = tabSidebar.SelectedIndex;
            if (activeTab == 0) // 地形
            {
                if (_copiedTerrain.HasValue)
                {
                    cell.Terrain = _copiedTerrain.Value;
                    cell.Attr = CloneJson(_copiedAttr);
                    cell.AttrA2 = CloneJson(_copiedAttrA2);
                    cell.AttrA3 = CloneJson(_copiedAttrA3);

                    // Sync to stage tiles list
                    _stage.MapTerrain.Tiles[cellIndex] = cell.Terrain;
                }
                else
                {
                    // Fallback to painting from the current UI controls
                    int baseT = (int)(nudBaseTerrain.Value ?? 0);
                    int variation = (int)(nudVariation.Value ?? 0);

                    ushort terrain = 0;
                    terrain |= (ushort)(baseT & 7);
                    terrain |= (ushort)((variation & 0x1F) << 3);

                    if (!(chkPlayableFlag.IsChecked ?? true)) terrain |= (ushort)(1 << 8);
                    if (chkOceanFlag.IsChecked ?? false) terrain |= (ushort)(2 << 8);

                    bool slot1Checked = chkSlot1.IsChecked ?? false;
                    if (slot1Checked)
                    {
                        terrain |= (ushort)(4 << 8);
                        if (cell.Attr == null) cell.Attr = new TileAttrModel();
                        cell.Attr.Byte0 = (sbyte)(byte)(nudSlot1Id.Value ?? 0);
                        cell.Attr.Byte1 = (sbyte)(byte)(nudSlot1Val.Value ?? 0);
                        cell.Attr.Byte2 = (sbyte)(nudSlot1Dx.Value ?? 0);
                        cell.Attr.Byte3 = (sbyte)(nudSlot1Dy.Value ?? 0);
                    }
                    else
                    {
                        cell.Attr = new TileAttrModel { Byte0 = 0, Byte1 = 0, Byte2 = 0, Byte3 = 0 };
                    }

                    bool slot2Checked = chkSlot2.IsChecked ?? false;
                    if (slot2Checked)
                    {
                        terrain |= (ushort)(8 << 8);
                        if (cell.AttrA2 == null) cell.AttrA2 = new TileAttrModel();
                        cell.AttrA2.Byte0 = (sbyte)(byte)(nudSlot2Id.Value ?? 0);
                        cell.AttrA2.Byte1 = (sbyte)(byte)(nudSlot2Val.Value ?? 0);
                        cell.AttrA2.Byte2 = (sbyte)(nudSlot2Dx.Value ?? 0);
                        cell.AttrA2.Byte3 = (sbyte)(nudSlot2Dy.Value ?? 0);
                    }
                    else
                    {
                        cell.AttrA2 = null;
                    }

                    bool slot3Checked = chkSlot3.IsChecked ?? false;
                    if (slot3Checked)
                    {
                        terrain |= (ushort)(16 << 8);
                        if (cell.AttrA3 == null) cell.AttrA3 = new TileAttrModel();
                        cell.AttrA3.Byte0 = (sbyte)(byte)(nudSlot3Id.Value ?? 0);
                        cell.AttrA3.Byte1 = (sbyte)(byte)(nudSlot3Val.Value ?? 0);
                        cell.AttrA3.Byte2 = (sbyte)(nudSlot3Dx.Value ?? 0);
                        cell.AttrA3.Byte3 = (sbyte)(nudSlot3Dy.Value ?? 0);
                    }
                    else
                    {
                        cell.AttrA3 = null;
                    }

                    cell.Terrain = terrain;
                    _stage.MapTerrain.Tiles[cellIndex] = terrain;
                }
            }
            else if (activeTab == 1) // 部队
            {
                if (cell.Unit != null)
                {
                    _stage.AIInfo?.Agents?.Remove(cell.Unit);
                    cell.Unit = null;
                }

                if (_copiedUnit != null)
                {
                    cell.Unit = CloneJson(_copiedUnit);
                    cell.Unit.AgentInfo.CellIdx = (ushort)cellIndex;
                    cell.Unit.AgentInfo.AgentId = GetNextUniqueAgentId();

                    if (_stage.AIInfo == null) _stage.AIInfo = new AIInfoModel();
                    if (_stage.AIInfo.Agents == null) _stage.AIInfo.Agents = new List<AIAgentModel>();
                    _stage.AIInfo.Agents.Add(cell.Unit);
                }
            }
            else if (activeTab == 2) // 建筑
            {
                if (cell.TriggerBldg != null)
                {
                    _stage.TriggerInfo?.Events?.Remove(cell.TriggerBldg);
                    cell.TriggerBldg = null;
                }
                if (cell.TriggerFort != null)
                {
                    _stage.TriggerInfo?.Events?.Remove(cell.TriggerFort);
                    cell.TriggerFort = null;
                }

                if (_copiedTriggerBldg != null)
                {
                    cell.TriggerBldg = CloneJson(_copiedTriggerBldg);
                    cell.TriggerBldg.TileIndex = (ushort)cellIndex;

                    if (_stage.TriggerInfo == null) _stage.TriggerInfo = new TriggerInfoModel();
                    if (_stage.TriggerInfo.Events == null) _stage.TriggerInfo.Events = new List<TriggerEventModel>();
                    _stage.TriggerInfo.Events.Add(cell.TriggerBldg);
                }

                if (_copiedTriggerFort != null)
                {
                    cell.TriggerFort = CloneJson(_copiedTriggerFort);
                    cell.TriggerFort.TileIndex = (ushort)cellIndex;

                    if (_stage.TriggerInfo == null) _stage.TriggerInfo = new TriggerInfoModel();
                    if (_stage.TriggerInfo.Events == null) _stage.TriggerInfo.Events = new List<TriggerEventModel>();
                    _stage.TriggerInfo.Events.Add(cell.TriggerFort);
                }
            }

            mapCanvas.InvalidateCellCache(cellIndex);
            
            if (cellIndex == _selectedCellIdx)
            {
                UpdateSidebarUi();
            }

            mapCanvas.InvalidateVisual();
        }

        private void UpdateSidebarUi()
        {
            if (_stage == null || _selectedCellIdx < 0 || _selectedCellIdx >= mapCanvas.Cells.Count)
            {
                lblCellCoords.Text = "坐标: 未选择";
                return;
            }

            _isUpdatingUi = true;
            var cell = mapCanvas.Cells[_selectedCellIdx];
            lblCellCoords.Text = $"坐标: ({cell.X}, {cell.Y})  索引: {cell.Index}";

            // 1. 地形属性反映
            ushort terrain = cell.Terrain;
            byte low = (byte)(terrain & 0xFF);
            byte high = (byte)(terrain >> 8);

            int baseT = low & 7;
            int variation = (low >> 3) & 0x1F;

            nudBaseTerrain.Value = baseT;
            lblBaseTerrainName.Text = "地质名称: " + GetBaseTerrainName(baseT);
            nudVariation.Value = variation;

            chkPlayableFlag.IsChecked = (high & 1) == 0;
            chkOceanFlag.IsChecked = (high & 2) != 0;

            // 插槽 1
            bool hasSlot1 = (high & 4) != 0;
            chkSlot1.IsChecked = hasSlot1;
            panelSlot1.IsEnabled = hasSlot1;
            if (cell.Attr != null)
            {
                nudSlot1Id.Value = cell.Attr.Byte0;
                nudSlot1Val.Value = cell.Attr.Byte1;
                nudSlot1Dx.Value = cell.Attr.Byte2;
                nudSlot1Dy.Value = cell.Attr.Byte3;
                lblSlot1Name.Text = "当前地物: " + GetDoodadName(cell.Attr.Byte0);
            }
            else
            {
                nudSlot1Id.Value = null;
                nudSlot1Val.Value = null;
                nudSlot1Dx.Value = null;
                nudSlot1Dy.Value = null;
                lblSlot1Name.Text = "当前地物: --";
            }

            // 插槽 2
            bool hasSlot2 = (high & 8) != 0;
            chkSlot2.IsChecked = hasSlot2;
            panelSlot2.IsEnabled = hasSlot2;
            if (cell.AttrA2 != null)
            {
                nudSlot2Id.Value = cell.AttrA2.Byte0;
                nudSlot2Val.Value = cell.AttrA2.Byte1;
                nudSlot2Dx.Value = cell.AttrA2.Byte2;
                nudSlot2Dy.Value = cell.AttrA2.Byte3;
                lblSlot2Name.Text = "当前地物: " + GetDoodadName(cell.AttrA2.Byte0);
            }
            else
            {
                nudSlot2Id.Value = null;
                nudSlot2Val.Value = null;
                nudSlot2Dx.Value = null;
                nudSlot2Dy.Value = null;
                lblSlot2Name.Text = "当前地物: --";
            }

            // 插槽 3
            bool hasSlot3 = (high & 16) != 0;
            chkSlot3.IsChecked = hasSlot3;
            panelSlot3.IsEnabled = hasSlot3;
            if (cell.AttrA3 != null)
            {
                nudSlot3Id.Value = cell.AttrA3.Byte0;
                nudSlot3Val.Value = cell.AttrA3.Byte1;
                nudSlot3Dx.Value = cell.AttrA3.Byte2;
                nudSlot3Dy.Value = cell.AttrA3.Byte3;
                lblSlot3Name.Text = "当前地物: " + GetDoodadName(cell.AttrA3.Byte0);
            }
            else
            {
                nudSlot3Id.Value = null;
                nudSlot3Val.Value = null;
                nudSlot3Dx.Value = null;
                nudSlot3Dy.Value = null;
                lblSlot3Name.Text = "当前地物: --";
            }

            // 2. 部队属性反映
            chkHasUnit.IsChecked = cell.Unit != null;
            panelUnit.IsEnabled = cell.Unit != null;
            if (cell.Unit != null && cell.Unit.AgentInfo != null)
            {
                var info = cell.Unit.AgentInfo;
                nudUnitId.Value = info.UnitId;
                lblUnitName.Text = "兵种名称: " + GameSettings.GetUnitName(info.UnitId ?? 0);
                nudUnitFaction.Value = info.FactionId;
                nudUnitHp.Value = info.HP;
                nudUnitMaxHp.Value = info.MaxHP;
                
                if (info.StackCount.HasValue)
                {
                    ushort sc = info.StackCount.Value;
                    nudUnitLevel.Value = sc & 0xFF;
                    nudUnitStack.Value = sc >> 8;
                }
                else
                {
                    nudUnitLevel.Value = null;
                    nudUnitStack.Value = null;
                }

                if (info.Val5.HasValue)
                {
                    ushort v5 = info.Val5.Value;
                    nudUnitDirection.Value = v5 & 0xFF;
                    nudUnitMobility.Value = (v5 >> 8) & 0xFF;
                }
                else
                {
                    nudUnitDirection.Value = null;
                    nudUnitMobility.Value = null;
                }

                nudUnitVal8.Value = info.Val8;
                nudUnitVal9.Value = info.Val9;
                nudUnitAgentId.Value = info.AgentId;

                // PlayMode decoding
                int? playMode = null;
                if (cell.Unit.ExtensionData != null && cell.Unit.ExtensionData.TryGetValue("field_2", out var pmVal) && pmVal != null)
                {
                    try { playMode = Convert.ToInt32(pmVal.ToString()); } catch {}
                }
                nudUnitAiTarget.Value = playMode; // 关键目标 (Left)
                nudUnitPlayMode.Value = cell.Unit.Morale; // 初始士气 (Right)
                nudUnitFactionExtra.Value = cell.Unit.FactionIdExtra;

                // AI Behavior
                if (cell.Unit.Behavior != null && cell.Unit.Behavior.ExtensionData != null && cell.Unit.Behavior.ExtensionData.Count > 0)
                {
                    chkUnitBehavior.IsChecked = true;
                    panelUnitBehavior.IsEnabled = true;
                    nudUnitBehaviorField0.Value = (decimal?)GetBehaviorFieldNullable(cell.Unit.Behavior.ExtensionData, "field_0");
                    nudUnitBehaviorId.Value = (decimal?)GetBehaviorFieldNullable(cell.Unit.Behavior.ExtensionData, "field_1");
                    nudUnitBehaviorField2.Value = (decimal?)GetBehaviorFieldNullable(cell.Unit.Behavior.ExtensionData, "field_2");
                    nudUnitBehaviorRadius.Value = (decimal?)GetBehaviorFieldNullable(cell.Unit.Behavior.ExtensionData, "field_3");
                    nudUnitBehaviorField4.Value = (decimal?)GetBehaviorFieldNullable(cell.Unit.Behavior.ExtensionData, "field_4");
                    nudUnitBehaviorCenter.Value = (decimal?)GetBehaviorFieldNullable(cell.Unit.Behavior.ExtensionData, "field_5");
                    nudUnitBehaviorField6.Value = (decimal?)GetBehaviorFieldNullable(cell.Unit.Behavior.ExtensionData, "field_6");
                }
                else
                {
                    chkUnitBehavior.IsChecked = false;
                    panelUnitBehavior.IsEnabled = false;
                    nudUnitBehaviorField0.Value = null;
                    nudUnitBehaviorId.Value = null;
                    nudUnitBehaviorField2.Value = null;
                    nudUnitBehaviorRadius.Value = null;
                    nudUnitBehaviorField4.Value = null;
                    nudUnitBehaviorCenter.Value = null;
                    nudUnitBehaviorField6.Value = null;
                }

                // General (Table 11)
                string tbl11Key = SymbolManager.GetCustomName("AIAgent", "table_4");
                var dict11 = GetExtensionDict(cell.Unit.ExtensionData, tbl11Key) ?? GetExtensionDict(cell.Unit.ExtensionData, "extra_table_11");
                if (dict11 != null)
                {
                    chkUnitGeneral.IsChecked = true;
                    panelUnitGeneral.IsEnabled = true;
                    int? genId = null;
                    if (dict11.TryGetValue("general_id", out var gidVal) && gidVal != null)
                    {
                        try { genId = Convert.ToInt32(gidVal.ToString()); } catch {}
                    }
                    nudUnitGeneralId.Value = genId;
                    lblUnitGeneralName.Text = "将领姓名: " + (genId.HasValue ? GameSettings.GetGeneralName(genId.Value) : "--");

                    chkUnitGeneralActive.IsChecked = GetResilientBool(dict11, "AIAgentTable11", "bool_0", "is_active", "param1", false);

                    int? param2 = null;
                    if (dict11.TryGetValue("param2", out var p2Val) && p2Val != null)
                    {
                        try { param2 = Convert.ToInt32(p2Val.ToString()); } catch {}
                    }
                    nudUnitGeneralParam2.Value = param2;
                }
                else
                {
                    chkUnitGeneral.IsChecked = false;
                    panelUnitGeneral.IsEnabled = false;
                    nudUnitGeneralId.Value = null;
                    lblUnitGeneralName.Text = "将领姓名: 无";
                    chkUnitGeneralActive.IsChecked = false;
                    nudUnitGeneralParam2.Value = null;
                }

                // ExArmy
                string tbl10Key = SymbolManager.GetCustomName("AIAgent", "table_3");
                var dict10 = GetExtensionDict(cell.Unit.ExtensionData, tbl10Key) 
                    ?? GetExtensionDict(cell.Unit.ExtensionData, "extra_table_10")
                    ?? GetExtensionDict(cell.Unit.ExtensionData, "exArmy");
                if (dict10 != null)
                {
                    chkUnitExArmy.IsChecked = true;
                    panelUnitExArmy.IsEnabled = true;
                    int exArmyId = 0;
                    if (dict10.TryGetValue("field_0", out var exidVal) && exidVal != null)
                    {
                        try { exArmyId = Convert.ToInt32(exidVal.ToString()); } catch {}
                    }
                    nudUnitExArmyId.Value = exArmyId;
                    
                    int? exHp = null;
                    if (dict10.TryGetValue("field_2", out var exhpVal) && exhpVal != null)
                    {
                        try { exHp = Convert.ToInt32(exhpVal.ToString()); } catch {}
                    }
                    nudUnitExArmyHp.Value = exHp;

                    int? exMaxHp = null;
                    if (dict10.TryGetValue("field_3", out var exmhpVal) && exmhpVal != null)
                    {
                        try { exMaxHp = Convert.ToInt32(exmhpVal.ToString()); } catch {}
                    }
                    nudUnitExArmyMaxHp.Value = exMaxHp;

                    int? exF1 = null;
                    if (dict10.TryGetValue("field_1", out var exf1Val) && exf1Val != null)
                    {
                        try { exF1 = Convert.ToInt32(exf1Val.ToString()); } catch {}
                    }
                    nudUnitExArmyField1.Value = exF1;

                    int? exF4 = null;
                    if (dict10.TryGetValue("field_4", out var exf4Val) && exf4Val != null)
                    {
                        try { exF4 = Convert.ToInt32(exf4Val.ToString()); } catch {}
                    }
                    nudUnitExArmyField4.Value = exF4;

                    int? exF5 = null;
                    if (dict10.TryGetValue("field_5", out var exf5Val) && exf5Val != null)
                    {
                        try { exF5 = Convert.ToInt32(exf5Val.ToString()); } catch {}
                    }
                    nudUnitExArmyField5.Value = exF5;

                    lblUnitExArmyName.Text = "名称: " + GameSettings.GetUnitName(exArmyId);
                }
                else
                {
                    chkUnitExArmy.IsChecked = false;
                    panelUnitExArmy.IsEnabled = false;
                    nudUnitExArmyId.Value = null;
                    nudUnitExArmyHp.Value = null;
                    nudUnitExArmyMaxHp.Value = null;
                    nudUnitExArmyField1.Value = null;
                    nudUnitExArmyField4.Value = null;
                    nudUnitExArmyField5.Value = null;
                    lblUnitExArmyName.Text = "名称: --";
                }
            }

            // 3. 建筑/工事 映射
            bool hasBuilding = cell.TriggerBldg != null;
            // lblBldgHeader removed
            chkHasBuilding.IsChecked = hasBuilding;
            panelBuilding.IsEnabled = hasBuilding;
            if (cell.TriggerBldg != null && cell.TriggerBldg.DetailBldg?.BuildingData != null)
            {
                var bData = cell.TriggerBldg.DetailBldg.BuildingData;
                nudBldgOwner.Value = bData.Val1;
                nudBuildingId.Value = bData.BuildingId;
                lblBuildingName.Text = "名: " + (bData.BuildingId.HasValue ? GameSettings.GetBuildingName(bData.BuildingId.Value) : "--");
                nudBldgFlag.Value = bData.Val2;
                nudBldgExtraFlag.Value = bData.Owner;
                nudBldgDx.Value = bData.Dx;
                nudBldgDy.Value = bData.Dy;
                nudBldgField6.Value = cell.TriggerBldg.DetailBldg.Field6 != null ? Convert.ToDecimal(cell.TriggerBldg.DetailBldg.Field6.ToString()) : null;
            }
            else
            {
                nudBuildingId.Value = null;
                lblBuildingName.Text = "名: --";
                nudBldgOwner.Value = null;
                nudBldgDx.Value = null;
                nudBldgDy.Value = null;
                nudBldgFlag.Value = null;
                nudBldgExtraFlag.Value = null;
                nudBldgField6.Value = null;
            }

            // 工事 Fort
            bool hasFort = cell.TriggerFort != null;
            // lblFortHeader removed
            chkHasFort.IsChecked = hasFort;
            panelFort.IsEnabled = hasFort;
            btnDeleteFort.Content = hasFort ? "删除工事" : "添加工事";
            if (cell.TriggerFort != null && cell.TriggerFort.DetailFort != null)
            {
                nudFortType.Value = cell.TriggerFort.DetailFort.FortId;
                lblFortTypeName.Text = "工事名称: " + (cell.TriggerFort.DetailFort.FortId.HasValue ? GameSettings.GetFortName(cell.TriggerFort.DetailFort.FortId.Value) : "空");
                nudFortField1.Value = cell.TriggerFort.Field1;
                nudFortField3.Value = GetResilientInt(cell.TriggerFort.DetailFort.ExtensionData, "TriggerEventFort", "table_30", "field_3", null, 0);
            }
            else
            {
                nudFortType.Value = null;
                lblFortTypeName.Text = "工事名称: 空";
                nudFortField1.Value = null;
                nudFortField3.Value = null;
            }
            _isUpdatingUi = false;
        }

        private void OnTerrainPropertyChanged()
        {
            if (_isUpdatingUi || _stage == null || _selectedCellIdx < 0) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];

            int baseT = (int)(nudBaseTerrain.Value ?? 0);
            int variation = (int)(nudVariation.Value ?? 0);

            ushort terrain = 0;
            terrain |= (ushort)(baseT & 7);
            terrain |= (ushort)((variation & 0x1F) << 3);

            if (!(chkPlayableFlag.IsChecked ?? true)) terrain |= (ushort)(1 << 8);
            if (chkOceanFlag.IsChecked ?? false) terrain |= (ushort)(2 << 8);

            // Slot 1
            bool slot1Checked = chkSlot1.IsChecked ?? false;
            panelSlot1.IsEnabled = slot1Checked;
            if (slot1Checked)
            {
                terrain |= (ushort)(4 << 8);
                if (cell.Attr == null) cell.Attr = new TileAttrModel();
                cell.Attr.Byte0 = (sbyte)(byte)(nudSlot1Id.Value ?? 0);
                cell.Attr.Byte1 = (sbyte)(byte)(nudSlot1Val.Value ?? 0);
                cell.Attr.Byte2 = (sbyte)(nudSlot1Dx.Value ?? 0);
                cell.Attr.Byte3 = (sbyte)(nudSlot1Dy.Value ?? 0);
            }
            else
            {
                cell.Attr = new TileAttrModel { Byte0 = 0, Byte1 = 0, Byte2 = 0, Byte3 = 0 };
            }

            // Slot 2
            bool slot2Checked = chkSlot2.IsChecked ?? false;
            panelSlot2.IsEnabled = slot2Checked;
            if (slot2Checked)
            {
                terrain |= (ushort)(8 << 8);
                if (cell.AttrA2 == null) cell.AttrA2 = new TileAttrModel();
                cell.AttrA2.Byte0 = (sbyte)(byte)(nudSlot2Id.Value ?? 0);
                cell.AttrA2.Byte1 = (sbyte)(byte)(nudSlot2Val.Value ?? 0);
                cell.AttrA2.Byte2 = (sbyte)(nudSlot2Dx.Value ?? 0);
                cell.AttrA2.Byte3 = (sbyte)(nudSlot2Dy.Value ?? 0);
            }
            else
            {
                cell.AttrA2 = null;
            }

            // Slot 3
            bool slot3Checked = chkSlot3.IsChecked ?? false;
            panelSlot3.IsEnabled = slot3Checked;
            if (slot3Checked)
            {
                terrain |= (ushort)(16 << 8);
                if (cell.AttrA3 == null) cell.AttrA3 = new TileAttrModel();
                cell.AttrA3.Byte0 = (sbyte)(byte)(nudSlot3Id.Value ?? 0);
                cell.AttrA3.Byte1 = (sbyte)(byte)(nudSlot3Val.Value ?? 0);
                cell.AttrA3.Byte2 = (sbyte)(nudSlot3Dx.Value ?? 0);
                cell.AttrA3.Byte3 = (sbyte)(nudSlot3Dy.Value ?? 0);
            }
            else
            {
                cell.AttrA3 = null;
            }

            cell.Terrain = terrain;
            _stage.MapTerrain.Tiles[_selectedCellIdx] = terrain;
            mapCanvas.InvalidateCellCache(_selectedCellIdx);

            lblBaseTerrainName.Text = "地质名称: " + GetBaseTerrainName(baseT);
            if (cell.Attr != null) lblSlot1Name.Text = "当前地物: " + GetDoodadName(cell.Attr.Byte0);
            if (cell.AttrA2 != null) lblSlot2Name.Text = "当前地物: " + GetDoodadName(cell.AttrA2.Byte0);
            if (cell.AttrA3 != null) lblSlot3Name.Text = "当前地物: " + GetDoodadName(cell.AttrA3.Byte0);

            mapCanvas.InvalidateVisual();
        }

        private void OnUnitPropertyChanged()
        {
            if (_isUpdatingUi || _stage == null || _selectedCellIdx < 0) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];

            bool hasUnit = chkHasUnit.IsChecked ?? false;
            panelUnit.IsEnabled = hasUnit;

            if (hasUnit)
            {
                if (cell.Unit == null)
                {
                    cell.Unit = new AIAgentModel
                    {
                        AgentInfo = new AgentInfoModel { CellIdx = (ushort)_selectedCellIdx },
                        Behavior = null
                    };
                    if (_stage.AIInfo?.Agents == null) _stage.AIInfo.Agents = new List<AIAgentModel>();
                    _stage.AIInfo.Agents.Add(cell.Unit);
                }

                var info = cell.Unit.AgentInfo;
                info.UnitId = (ushort?)(nudUnitId.Value);
                info.FactionId = (ushort?)(nudUnitFaction.Value);
                info.HP = (ushort?)(nudUnitHp.Value);
                info.MaxHP = (ushort?)(nudUnitMaxHp.Value);
                
                // Pack Level and StackCount into StackCount
                int level = (int)(nudUnitLevel.Value ?? 0);
                int stack = (int)(nudUnitStack.Value ?? 0);
                info.StackCount = (ushort)((level & 0xFF) | (stack << 8));

                // Pack Direction and Mobility into Val5
                int direction = (int)(nudUnitDirection.Value ?? 0);
                int mobility = (int)(nudUnitMobility.Value ?? 0);
                info.Val5 = (ushort)((direction & 0xFF) | (mobility << 8));

                info.Val8 = (ushort?)(nudUnitVal8.Value);
                info.Val9 = (ushort?)(nudUnitVal9.Value);
                info.AgentId = (ushort?)(nudUnitAgentId.Value);

                // 关键目标 (Left - nudUnitAiTarget -> field_2)
                byte pm = (byte)(nudUnitAiTarget.Value ?? 0);
                if (cell.Unit.ExtensionData == null) cell.Unit.ExtensionData = new Dictionary<string, object>();
                cell.Unit.ExtensionData["field_2"] = pm;

                // 初始士气 (Right - nudUnitPlayMode -> Morale)
                cell.Unit.Morale = (byte?)(nudUnitPlayMode.Value);
                cell.Unit.FactionIdExtra = (byte?)(nudUnitFactionExtra.Value);

                // AI Behavior
                bool behaviorChecked = chkUnitBehavior.IsChecked ?? false;
                panelUnitBehavior.IsEnabled = behaviorChecked;
                if (behaviorChecked)
                {
                    if (cell.Unit.Behavior == null) cell.Unit.Behavior = new AIAgentBehaviorModel();
                    cell.Unit.Behavior.ExtensionData = new Dictionary<string, object>();

                    void SetBehaviorField(string fieldName, object val)
                    {
                        var keys = GetFieldKeys("AIAgentBehavior", fieldName);
                        foreach (var key in keys)
                        {
                            SetExtensionValueOrRemove(cell.Unit.Behavior.ExtensionData, key, val);
                        }
                    }

                    SetBehaviorField("field_0", (byte?)(nudUnitBehaviorField0.Value));
                    SetBehaviorField("field_1", (ushort?)(nudUnitBehaviorId.Value));
                    SetBehaviorField("field_2", (short?)(nudUnitBehaviorField2.Value));
                    SetBehaviorField("field_3", (byte?)(nudUnitBehaviorRadius.Value));
                    SetBehaviorField("field_4", (byte?)(nudUnitBehaviorField4.Value));
                    SetBehaviorField("field_5", (ushort?)(nudUnitBehaviorCenter.Value));
                    SetBehaviorField("field_6", (short?)(nudUnitBehaviorField6.Value));
                }
                else
                {
                    cell.Unit.Behavior = null;
                }

                // General (T11)
                bool generalChecked = chkUnitGeneral.IsChecked ?? false;
                panelUnitGeneral.IsEnabled = generalChecked;
                string tbl11Key = SymbolManager.GetCustomName("AIAgent", "table_4");
                if (cell.Unit.ExtensionData == null) cell.Unit.ExtensionData = new Dictionary<string, object>();
                cell.Unit.ExtensionData.Remove(tbl11Key);
                cell.Unit.ExtensionData.Remove("extra_table_11");

                if (generalChecked)
                {
                    var dict11 = new Dictionary<string, object>();
                    if (nudUnitGeneralId.Value.HasValue)
                    {
                        ushort gid = (ushort)nudUnitGeneralId.Value.Value;
                        dict11["general_id"] = gid;
                        lblUnitGeneralName.Text = "将领姓名: " + GameSettings.GetGeneralName(gid);
                    }
                    else
                    {
                        lblUnitGeneralName.Text = "将领姓名: 无";
                    }

                    if (chkUnitGeneralActive.IsChecked ?? false)
                    {
                        dict11["param1"] = true;
                    }

                    if (nudUnitGeneralParam2.Value.HasValue)
                    {
                        dict11["param2"] = (byte)nudUnitGeneralParam2.Value.Value;
                    }

                    cell.Unit.ExtensionData[tbl11Key] = dict11;
                }
                else
                {
                    lblUnitGeneralName.Text = "将领姓名: 无";
                }

                // ExArmy (T10)
                bool exArmyChecked = chkUnitExArmy.IsChecked ?? false;
                panelUnitExArmy.IsEnabled = exArmyChecked;
                string tbl10Key = SymbolManager.GetCustomName("AIAgent", "table_3");
                cell.Unit.ExtensionData.Remove(tbl10Key);
                cell.Unit.ExtensionData.Remove("extra_table_10");
                cell.Unit.ExtensionData.Remove("exArmy");

                if (exArmyChecked)
                {
                    var dict10 = new Dictionary<string, object>();
                    int exArmyId = (int)(nudUnitExArmyId.Value ?? 0);
                    dict10["field_0"] = (ushort)exArmyId;
                    if (nudUnitExArmyHp.Value.HasValue) dict10["field_2"] = (ushort)nudUnitExArmyHp.Value.Value;
                    if (nudUnitExArmyMaxHp.Value.HasValue) dict10["field_3"] = (ushort)nudUnitExArmyMaxHp.Value.Value;
                    if (nudUnitExArmyField1.Value.HasValue) dict10["field_1"] = (byte)nudUnitExArmyField1.Value.Value;
                    if (nudUnitExArmyField4.Value.HasValue) dict10["field_4"] = (ushort)nudUnitExArmyField4.Value.Value;
                    if (nudUnitExArmyField5.Value.HasValue) dict10["field_5"] = (ushort)nudUnitExArmyField5.Value.Value;

                    cell.Unit.ExtensionData[tbl10Key] = dict10;
                    lblUnitExArmyName.Text = "名称: " + GameSettings.GetUnitName(exArmyId);
                }
                else
                {
                    lblUnitExArmyName.Text = "名称: --";
                }

                lblUnitName.Text = "兵种名称: " + GameSettings.GetUnitName(info.UnitId ?? 0);
            }
            else
            {
                if (cell.Unit != null)
                {
                    _stage.AIInfo?.Agents?.Remove(cell.Unit);
                    cell.Unit = null;
                }
            }

            if (_selectedCellIdx >= 0)
            {
                mapCanvas.InvalidateCellCache(_selectedCellIdx);
            }
            mapCanvas.InvalidateVisual();
        }

        private void OnBuildingPropertyChanged()
        {
            if (_isUpdatingUi || _stage == null || _selectedCellIdx < 0) return;
            var cell = mapCanvas.Cells[_selectedCellIdx];

            // 1. Building properties
            bool hasBuilding = chkHasBuilding.IsChecked ?? false;
            panelBuilding.IsEnabled = hasBuilding;

            if (hasBuilding)
            {
                if (cell.TriggerBldg == null)
                {
                    cell.TriggerBldg = new TriggerEventModel
                    {
                        TileIndex = (ushort)_selectedCellIdx,
                        Field1 = 1,
                        DetailBldg = new TriggerEventBldgModel
                        {
                            BuildingData = new BuildingDataModel { Val1 = 0, Val2 = 0 }
                        }
                    };
                    if (_stage.TriggerInfo?.Events == null) _stage.TriggerInfo.Events = new List<TriggerEventModel>();
                    _stage.TriggerInfo.Events.Add(cell.TriggerBldg);
                }

                if (cell.TriggerBldg.DetailBldg == null) cell.TriggerBldg.DetailBldg = new TriggerEventBldgModel();
                if (cell.TriggerBldg.DetailBldg.BuildingData == null) cell.TriggerBldg.DetailBldg.BuildingData = new BuildingDataModel();

                var bData = cell.TriggerBldg.DetailBldg.BuildingData;
                bData.Val1 = (ushort?)(nudBldgOwner.Value);
                bData.BuildingId = (ushort?)(nudBuildingId.Value);
                bData.Val2 = (byte?)(nudBldgFlag.Value);
                bData.Owner = (byte?)(int?)(nudBldgExtraFlag.Value);
                bData.Dx = (sbyte?)(int?)(nudBldgDx.Value);
                bData.Dy = (sbyte?)(int?)(nudBldgDy.Value);
                cell.TriggerBldg.DetailBldg.Field6 = (byte?)(nudBldgField6.Value);

                lblBuildingName.Text = "名: " + (bData.BuildingId.HasValue ? GameSettings.GetBuildingName(bData.BuildingId.Value) : "--");
            }
            else
            {
                if (cell.TriggerBldg != null)
                {
                    _stage.TriggerInfo?.Events?.Remove(cell.TriggerBldg);
                    cell.TriggerBldg = null;
                }
            }

            // 2. 要塞 Fort properties
            bool hasFort = chkHasFort.IsChecked ?? false;
            panelFort.IsEnabled = hasFort;
            btnDeleteFort.Content = hasFort ? "删除工事" : "添加工事";

            if (hasFort)
            {
                if (cell.TriggerFort == null)
                {
                    cell.TriggerFort = new TriggerEventModel
                    {
                        TileIndex = (ushort)_selectedCellIdx,
                        Field1 = 2,
                        DetailFort = new TriggerEventFortModel()
                    };
                    if (_stage.TriggerInfo?.Events == null) _stage.TriggerInfo.Events = new List<TriggerEventModel>();
                    _stage.TriggerInfo.Events.Add(cell.TriggerFort);
                }

                if (cell.TriggerFort.DetailFort == null) cell.TriggerFort.DetailFort = new TriggerEventFortModel();
                if (cell.TriggerFort.DetailFort.ExtensionData == null) cell.TriggerFort.DetailFort.ExtensionData = new Dictionary<string, object>();

                cell.TriggerFort.DetailFort.FortId = (ushort?)(nudFortType.Value);
                cell.TriggerFort.Field1 = (ushort)(nudFortField1.Value ?? 0);
                cell.TriggerFort.DetailFort.ExtensionData["field_3"] = (byte)(nudFortField3.Value ?? 0);

                lblFortTypeName.Text = "工事名称: " + (cell.TriggerFort?.DetailFort?.FortId != null ? GameSettings.GetFortName(cell.TriggerFort.DetailFort.FortId.Value) : "空");
            }
            else
            {
                if (cell.TriggerFort != null)
                {
                    _stage.TriggerInfo?.Events?.Remove(cell.TriggerFort);
                    cell.TriggerFort = null;
                lblFortTypeName.Text = "工事名称: 空";
                }
            }

            if (_selectedCellIdx >= 0)
            {
                mapCanvas.InvalidateCellCache(_selectedCellIdx);
            }
            mapCanvas.InvalidateVisual();
        }

        private void RefreshTargetsList()
        {
            if (_stage?.StageMetadata?.Targets == null)
            {
                lstStageTargets.ItemsSource = null;
                panelTargetEdit.IsEnabled = false;
                return;
            }

            var items = new List<string>();
            foreach (var t in _stage.StageMetadata.Targets)
            {
                string typeStr = t.TargetType.HasValue ? t.TargetType.Value.ToString() : "空";
                string valStr = t.TargetValue.HasValue ? t.TargetValue.Value.ToString() : "空";
                string p1Str = t.Param1.HasValue ? t.Param1.Value.ToString() : "空";
                string p2Str = t.Param2.HasValue ? t.Param2.Value.ToString() : "空";
                string flagStr = t.Flag.HasValue ? t.Flag.Value.ToString() : "空";
                items.Add($"[类别:{typeStr}] [值:{valStr}] [参1:{p1Str}] [参2:{p2Str}] [标志:{flagStr}]");
            }

            int selIdx = lstStageTargets.SelectedIndex;
            lstStageTargets.ItemsSource = items;

            if (selIdx >= 0 && selIdx < items.Count)
                lstStageTargets.SelectedIndex = selIdx;
            else if (items.Count > 0)
                lstStageTargets.SelectedIndex = 0;
            else
            {
                lstStageTargets.SelectedIndex = -1;
                panelTargetEdit.IsEnabled = false;
            }
        }

        private void OnTargetSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi || _stage?.StageMetadata?.Targets == null) return;
            int idx = lstStageTargets.SelectedIndex;
            if (idx < 0 || idx >= _stage.StageMetadata.Targets.Count)
            {
                panelTargetEdit.IsEnabled = false;
                return;
            }

            panelTargetEdit.IsEnabled = true;
            _isUpdatingUi = true;

            var t = _stage.StageMetadata.Targets[idx];
            nudTargetType.Value = t.TargetType;
            nudTargetVal.Value = t.TargetValue;
            nudTargetParam1.Value = t.Param1;
            nudTargetParam2.Value = t.Param2;
            nudTargetFlag.Value = t.Flag;

            _isUpdatingUi = false;
        }

        private void OnAddTargetClicked(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_stage == null) return;
            if (_stage.StageMetadata == null) _stage.StageMetadata = new StageMetadataModel();
            if (_stage.StageMetadata.Targets == null) _stage.StageMetadata.Targets = new List<StageTargetModel>();

            _stage.StageMetadata.Targets.Add(new StageTargetModel
            {
                TargetType = 0,
                TargetValue = 0,
                Param1 = 0,
                Param2 = 0,
                Flag = 0
            });

            RefreshTargetsList();
            lstStageTargets.SelectedIndex = _stage.StageMetadata.Targets.Count - 1;
        }

        private void OnSaveTargetClicked(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_stage?.StageMetadata?.Targets == null) return;
            int idx = lstStageTargets.SelectedIndex;
            if (idx < 0 || idx >= _stage.StageMetadata.Targets.Count) return;

            var t = _stage.StageMetadata.Targets[idx];
            t.TargetType = (ushort?)(nudTargetType.Value);
            t.TargetValue = (short?)(nudTargetVal.Value);
            t.Param1 = (ushort?)(nudTargetParam1.Value);
            t.Param2 = (ushort?)(nudTargetParam2.Value);
            t.Flag = (byte?)(nudTargetFlag.Value);

            RefreshTargetsList();
        }

        private void OnDeleteTargetClicked(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_stage?.StageMetadata?.Targets == null) return;
            int idx = lstStageTargets.SelectedIndex;
            if (idx < 0 || idx >= _stage.StageMetadata.Targets.Count) return;

            _stage.StageMetadata.Targets.RemoveAt(idx);
            RefreshTargetsList();
        }

        
        private byte? GetFactionByte(byte? propVal, Dictionary<string, object>? ext1, Dictionary<string, object>? ext2, params string[] keys)
        {
            if (propVal.HasValue) return propVal.Value;
            if (ext1 != null)
            {
                foreach (var k in keys)
                {
                    if (ext1.TryGetValue(k, out var v) && v != null)
                    {
                        try { return Convert.ToByte(v.ToString()); } catch {}
                    }
                }
            }
            if (ext2 != null)
            {
                foreach (var k in keys)
                {
                    if (ext2.TryGetValue(k, out var v) && v != null)
                    {
                        try { return Convert.ToByte(v.ToString()); } catch {}
                    }
                }
            }
            return null;
        }

        private ushort? GetFactionUshort(ushort? propVal, Dictionary<string, object>? ext1, Dictionary<string, object>? ext2, params string[] keys)
        {
            if (propVal.HasValue && propVal.Value != 0) return propVal.Value;
            if (ext1 != null)
            {
                foreach (var k in keys)
                {
                    if (ext1.TryGetValue(k, out var v) && v != null)
                    {
                        try { return Convert.ToUInt16(v.ToString()); } catch {}
                    }
                }
            }
            if (ext2 != null)
            {
                foreach (var k in keys)
                {
                    if (ext2.TryGetValue(k, out var v) && v != null)
                    {
                        try { return Convert.ToUInt16(v.ToString()); } catch {}
                    }
                }
            }
            if (propVal.HasValue) return propVal.Value;
            return null;
        }

        private void OnFactionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi || _stage?.FactionInfo?.Factions == null) return;
            int idx = lstFactions.SelectedIndex;
            if (idx < 0 || idx >= _stage.FactionInfo.Factions.Count)
            {
                panelFactionEdit.IsEnabled = false;
                return;
            }

            panelFactionEdit.IsEnabled = true;
            _isUpdatingUi = true;

            var f = _stage.FactionInfo.Factions[idx];
            var info = f.Info;

            nudFactionId.Value = info?.FactionId;
            nudFactionCamp.Value = GetFactionByte(info?.Camp, info?.ExtensionData, f.ExtensionData, "camp", "ubyte_0");

            nudFactionCountry.Value = info?.CountryId;

            nudFactionIsAI.Value = GetFactionByte(info?.IsAI, info?.ExtensionData, f.ExtensionData, "is_ai", "ubyte_1");
            nudFactionGeneralLimit.Value = GetFactionByte(info?.GeneralLimit, info?.ExtensionData, f.ExtensionData, "general_limit", "ubyte_2");
            nudFactionAlign1.Value = GetFactionByte(info?.Align1, info?.ExtensionData, f.ExtensionData, "align_1", "ubyte_3");
            nudFactionGold.Value = info?.InitialGold;
            nudFactionTech.Value = info?.InitialTech;
            nudFactionIncomeMod.Value = (decimal?)info?.IncomeModifier;
            nudFactionDamageMod.Value = (decimal?)info?.DamageModifier;
            nudFactionHpMod.Value = (decimal?)info?.HPModifier;

            uint colorVal = info?.Color ?? 0;
            if (info?.Color != null)
            {
                nudFactionColorR.Value = (int)((colorVal >> 24) & 0xFF);
                nudFactionColorG.Value = (int)((colorVal >> 16) & 0xFF);
                nudFactionColorB.Value = (int)((colorVal >> 8) & 0xFF);
                nudFactionColorA.Value = (int)(colorVal & 0xFF);
            }
            else
            {
                nudFactionColorR.Value = null;
                nudFactionColorG.Value = null;
                nudFactionColorB.Value = null;
                nudFactionColorA.Value = null;
            }

            nudFactionAlign2.Value = GetFactionUshort(info?.Align2, info?.ExtensionData, f.ExtensionData, "align_2", "ushort_2");
            nudFactionConfigId.Value = GetFactionUshort(info?.ConfigId, info?.ExtensionData, f.ExtensionData, "config_id", "ushort_3");
            nudFactionGeneralFlag.Value = GetFactionByte(f.GeneralFlag != 0 ? f.GeneralFlag : (byte?)null, f.ExtensionData, info?.ExtensionData, "val1", "general_flag", "ubyte_16");
            nudFactionConfigRef.Value = GetFactionUshort(f.ConfigRef != 0 ? f.ConfigRef : (ushort?)null, f.ExtensionData, info?.ExtensionData, "val2", "config_ref", "ushort_26");

            _isUpdatingUi = false;
        }

        private void OnFactionPropertyChanged()
        {
            if (_isUpdatingUi || _stage?.FactionInfo?.Factions == null) return;
            int idx = lstFactions.SelectedIndex;
            if (idx < 0 || idx >= _stage.FactionInfo.Factions.Count) return;

            var f = _stage.FactionInfo.Factions[idx];
            if (f.Info == null) f.Info = new FactionMetadataModel();
            var info = f.Info;

            info.FactionId = (ushort)(nudFactionId.Value ?? 0);
            info.Camp = (byte?)(nudFactionCamp.Value);

            info.CountryId = (ushort)(nudFactionCountry.Value ?? 0);

            info.IsAI = (byte?)(nudFactionIsAI.Value);
                info.GeneralLimit = (byte?)(nudFactionGeneralLimit.Value);
                info.Align1 = (byte?)(nudFactionAlign1.Value);
                info.Align2 = (ushort?)(nudFactionAlign2.Value);
            info.InitialGold = (uint?)(nudFactionGold.Value);
            info.InitialTech = (uint?)(nudFactionTech.Value);
            info.IncomeModifier = (float?)(nudFactionIncomeMod.Value);
            info.DamageModifier = (float?)(nudFactionDamageMod.Value);
            info.HPModifier = (float?)(nudFactionHpMod.Value);

            int r = (int)(nudFactionColorR.Value ?? -1);
            int g = (int)(nudFactionColorG.Value ?? -1);
            int b = (int)(nudFactionColorB.Value ?? -1);
            int a = (int)(nudFactionColorA.Value ?? -1);

            if (r >= 0 || g >= 0 || b >= 0 || a >= 0)
            {
                uint ur = (uint)Math.Clamp(r, 0, 255);
                uint ug = (uint)Math.Clamp(g, 0, 255);
                uint ub = (uint)Math.Clamp(b, 0, 255);
                uint ua = (uint)Math.Clamp(a, 0, 255);
                info.Color = (ur << 24) | (ug << 16) | (ub << 8) | ua;
            }
            else
            {
                info.Color = null;
            }

            f.GeneralFlag = (byte)(nudFactionGeneralFlag.Value ?? 0);
            info.ConfigId = (ushort?)(nudFactionConfigId.Value);
            f.ConfigRef = (ushort)(nudFactionConfigRef.Value ?? 0);

            _isUpdatingUi = true;
            lstFactions.Items[idx] = $"势力 {info.FactionId}: {GameSettings.GetCountryName(info.CountryId)}";
            _isUpdatingUi = false;
            mapCanvas.InvalidateVisual();
        }

        private void OnAddFactionClicked(object sender, RoutedEventArgs e)
        {
            if (_stage == null) return;
            if (_stage.FactionInfo == null) _stage.FactionInfo = new FactionInfoModel();
            if (_stage.FactionInfo.Factions == null) _stage.FactionInfo.Factions = new List<FactionModel>();

            ushort newFactionId = 0;
            if (_stage.FactionInfo.Factions.Count > 0)
            {
                newFactionId = (ushort)(_stage.FactionInfo.Factions.Max(f => f.Info?.FactionId ?? 0) + 1);
            }

            var newFaction = new FactionModel
            {
                Info = new FactionMetadataModel
                {
                    FactionId = newFactionId,
                    CountryId = 0,
                    Camp = 0,
                    IsAI = 1,
                    InitialGold = 100,
                    InitialTech = 0,
                    IncomeModifier = 1.0f,
                    DamageModifier = 1.0f,
                    HPModifier = 1.0f,
                    Color = null
                },
                GeneralFlag = 0,
                ConfigRef = 0
            };

            _stage.FactionInfo.Factions.Add(newFaction);
            RefreshFactionList();
            lstFactions.SelectedIndex = _stage.FactionInfo.Factions.Count - 1;
        }

        
        private void OnMoveFactionUpClicked(object sender, RoutedEventArgs e)
        {
            if (_stage?.FactionInfo?.Factions == null) return;
            int oldIdx = lstFactions.SelectedIndex;
            if (oldIdx <= 0 || oldIdx >= _stage.FactionInfo.Factions.Count) return;

            int newIdx = oldIdx - 1;
            var faction = _stage.FactionInfo.Factions[oldIdx];
            _stage.FactionInfo.Factions.RemoveAt(oldIdx);
            _stage.FactionInfo.Factions.Insert(newIdx, faction);

            // Reorder FactionCards and FactionLimits if present
            if (_stage.FactionInfo.FactionCards != null && faction.Info != null)
            {
                var cardEntry = _stage.FactionInfo.FactionCards.FirstOrDefault(fc => fc.FactionId == faction.Info.FactionId);
                if (cardEntry != null)
                {
                    _stage.FactionInfo.FactionCards.Remove(cardEntry);
                    int targetIdx = Math.Min(newIdx, _stage.FactionInfo.FactionCards.Count);
                    _stage.FactionInfo.FactionCards.Insert(targetIdx, cardEntry);
                }
            }

            if (_stage.FactionInfo.FactionLimits != null && faction.Info != null)
            {
                var limitEntry = _stage.FactionInfo.FactionLimits.FirstOrDefault(fl => fl.FactionId == faction.Info.FactionId);
                if (limitEntry != null)
                {
                    _stage.FactionInfo.FactionLimits.Remove(limitEntry);
                    int targetIdx = Math.Min(newIdx, _stage.FactionInfo.FactionLimits.Count);
                    _stage.FactionInfo.FactionLimits.Insert(targetIdx, limitEntry);
                }
            }

            RefreshFactionList();
            lstFactions.SelectedIndex = newIdx;
            mapCanvas.InvalidateVisual();
        }

        private void OnMoveFactionDownClicked(object sender, RoutedEventArgs e)
        {
            if (_stage?.FactionInfo?.Factions == null) return;
            int oldIdx = lstFactions.SelectedIndex;
            if (oldIdx < 0 || oldIdx >= _stage.FactionInfo.Factions.Count - 1) return;

            int newIdx = oldIdx + 1;
            var faction = _stage.FactionInfo.Factions[oldIdx];
            _stage.FactionInfo.Factions.RemoveAt(oldIdx);
            _stage.FactionInfo.Factions.Insert(newIdx, faction);

            // Reorder FactionCards and FactionLimits if present
            if (_stage.FactionInfo.FactionCards != null && faction.Info != null)
            {
                var cardEntry = _stage.FactionInfo.FactionCards.FirstOrDefault(fc => fc.FactionId == faction.Info.FactionId);
                if (cardEntry != null)
                {
                    _stage.FactionInfo.FactionCards.Remove(cardEntry);
                    int targetIdx = Math.Min(newIdx, _stage.FactionInfo.FactionCards.Count);
                    _stage.FactionInfo.FactionCards.Insert(targetIdx, cardEntry);
                }
            }

            if (_stage.FactionInfo.FactionLimits != null && faction.Info != null)
            {
                var limitEntry = _stage.FactionInfo.FactionLimits.FirstOrDefault(fl => fl.FactionId == faction.Info.FactionId);
                if (limitEntry != null)
                {
                    _stage.FactionInfo.FactionLimits.Remove(limitEntry);
                    int targetIdx = Math.Min(newIdx, _stage.FactionInfo.FactionLimits.Count);
                    _stage.FactionInfo.FactionLimits.Insert(targetIdx, limitEntry);
                }
            }

            RefreshFactionList();
            lstFactions.SelectedIndex = newIdx;
            mapCanvas.InvalidateVisual();
        }

        private void OnDeleteFactionClicked(object sender, RoutedEventArgs e)
        {
            if (_stage?.FactionInfo?.Factions == null) return;
            int idx = lstFactions.SelectedIndex;
            if (idx < 0 || idx >= _stage.FactionInfo.Factions.Count) return;

            _stage.FactionInfo.Factions.RemoveAt(idx);
            RefreshFactionList();
            panelFactionEdit.IsEnabled = false;
        }

        private void RefreshFactionList()
        {
            _isUpdatingUi = true;
            lstFactions.Items.Clear();
            if (_stage?.FactionInfo?.Factions != null)
            {
                for (int i = 0; i < _stage.FactionInfo.Factions.Count; i++)
                {
                    var f = _stage.FactionInfo.Factions[i];
                    ushort countryId = f.Info?.CountryId ?? 0;
                    ushort factionId = f.Info?.FactionId ?? 0;
                    lstFactions.Items.Add($"势力 {factionId}: {GameSettings.GetCountryName(countryId)}");
                }
            }
            _isUpdatingUi = false;
        }

        private void RefreshReinforceList()
        {
            if (_stage?.BattleInfo?.ReinforcePoints == null)
            {
                lstReinforces.ItemsSource = null;
                panelReinforceEdit.IsEnabled = false;
                return;
            }

            var items = new List<string>();
            for (int i = 0; i < _stage.BattleInfo.ReinforcePoints.Count; i++)
            {
                var rp = _stage.BattleInfo.ReinforcePoints[i];
                string cellStr = rp.CellIdx.HasValue ? rp.CellIdx.Value.ToString() : "空";
                string facStr = rp.FactionId.HasValue ? rp.FactionId.Value.ToString() : "空";

                bool isKey = false;
                if (rp.IsKeyUnit != null)
                {
                    try { isKey = Convert.ToBoolean(rp.IsKeyUnit); } catch {}
                }
                string keyStr = isKey ? "是" : "否";
                string flagStr = rp.Flag.HasValue ? rp.Flag.Value.ToString() : "空";

                items.Add($"[位置:{cellStr}] [势力:{facStr}] [关键:{keyStr}] [标志:{flagStr}]");
            }

            int selIdx = lstReinforces.SelectedIndex;
            lstReinforces.ItemsSource = items;

            if (selIdx >= 0 && selIdx < items.Count)
                lstReinforces.SelectedIndex = selIdx;
            else if (items.Count > 0)
                lstReinforces.SelectedIndex = 0;
            else
            {
                lstReinforces.SelectedIndex = -1;
                panelReinforceEdit.IsEnabled = false;
            }
        }

        private void OnReinforceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi || _stage?.BattleInfo?.ReinforcePoints == null) return;
            int idx = lstReinforces.SelectedIndex;
            if (idx < 0 || idx >= _stage.BattleInfo.ReinforcePoints.Count)
            {
                panelReinforceEdit.IsEnabled = false;
                return;
            }

            panelReinforceEdit.IsEnabled = true;
            _isUpdatingUi = true;

            var rp = _stage.BattleInfo.ReinforcePoints[idx];
            nudRpCellIdx.Value = rp.CellIdx;
            nudRpFactionId.Value = rp.FactionId;

            bool isKey = false;
            if (rp.IsKeyUnit != null)
            {
                try { isKey = Convert.ToBoolean(rp.IsKeyUnit); } catch {}
            }
            chkRpIsKey.IsChecked = isKey;
            nudRpFlag.Value = rp.Flag;

            _isUpdatingUi = false;
        }

        private void OnReinforcePropertyChanged()
        {
            if (_isUpdatingUi || _stage?.BattleInfo?.ReinforcePoints == null) return;
            int idx = lstReinforces.SelectedIndex;
            if (idx < 0 || idx >= _stage.BattleInfo.ReinforcePoints.Count) return;

            var rp = _stage.BattleInfo.ReinforcePoints[idx];
            rp.CellIdx = (ushort?)(nudRpCellIdx.Value);
            rp.FactionId = (byte?)(nudRpFactionId.Value);
            rp.IsKeyUnit = chkRpIsKey.IsChecked ?? false;
            rp.Flag = (byte?)(nudRpFlag.Value);

            RefreshReinforceList();
        }

        private void OnAddReinforceClicked(object sender, RoutedEventArgs e)
        {
            if (_stage == null) return;
            if (_stage.BattleInfo == null) _stage.BattleInfo = new BattleInfoModel();
            if (_stage.BattleInfo.ReinforcePoints == null) _stage.BattleInfo.ReinforcePoints = new List<ReinforcePointModel>();

            _stage.BattleInfo.ReinforcePoints.Add(new ReinforcePointModel
            {
                CellIdx = 0,
                FactionId = 1,
                IsKeyUnit = false,
                Flag = 0
            });

            RefreshReinforceList();
            lstReinforces.SelectedIndex = _stage.BattleInfo.ReinforcePoints.Count - 1;
        }

        private void OnDeleteReinforceClicked(object sender, RoutedEventArgs e)
        {
            if (_stage?.BattleInfo?.ReinforcePoints == null) return;
            int idx = lstReinforces.SelectedIndex;
            if (idx < 0 || idx >= _stage.BattleInfo.ReinforcePoints.Count) return;

            _stage.BattleInfo.ReinforcePoints.RemoveAt(idx);
            RefreshReinforceList();
        }

        private void OnGlobalMetadataChanged()
        {
            if (_isUpdatingUi || _stage == null) return;
            if (_stage.StageMetadata != null)
            {
                _stage.StageMetadata.StageNum = (ushort)(nudStageNum.Value ?? 0);
            }
            _stage.Version = (ushort)(nudVersion.Value ?? 0);
            if (_stage.TriggerInfo != null)
            {
                _stage.TriggerInfo.Tag = (short)(nudTag.Value ?? 0);
            }
        }

        private void OnResizeMapClicked(object sender, RoutedEventArgs e)
        {
            if (_stage == null) return;
            ushort oldW = _stage.MapTerrain.Size.Width;
            ushort oldH = _stage.MapTerrain.Size.Height;
            ushort newW = (ushort)(nudMapW.Value ?? oldW);
            ushort newH = (ushort)(nudMapH.Value ?? oldH);

            if (newW == oldW && newH == oldH) return;

            try
            {
                var oldCells = mapCanvas.Cells;
                var newCells = new List<CellItem>();
                for (int idx = 0; idx < newW * newH; idx++)
                {
                    int x = idx % newW;
                    int y = idx / newW;
                    if (x < oldW && y < oldH)
                    {
                        var oldCell = oldCells[y * oldW + x];
                        oldCell.Index = idx;
                        oldCell.X = x;
                        oldCell.Y = y;
                        newCells.Add(oldCell);
                    }
                    else
                    {
                        newCells.Add(new CellItem
                        {
                            Index = idx,
                            X = x,
                            Y = y,
                            Terrain = 0,
                            Attr = new TileAttrModel { Byte0 = 0, Byte1 = 0, Byte2 = 0, Byte3 = 0 }
                        });
                    }
                }

                var newTiles = new List<ushort>();
                var newAttributes = new List<TileAttrModel>();
                foreach (var cell in newCells)
                {
                    newTiles.Add(cell.Terrain);
                    byte v6 = (byte)(cell.Terrain >> 8);
                    if ((v6 & 4) != 0)
                    {
                        newAttributes.Add(cell.Attr ?? new TileAttrModel { Byte0 = 0, Byte1 = 0, Byte2 = 0, Byte3 = 0 });
                    }
                    if ((v6 & 8) != 0)
                    {
                        newAttributes.Add(cell.AttrA2 ?? new TileAttrModel { Byte0 = 0, Byte1 = 0, Byte2 = 0, Byte3 = 0 });
                    }
                    if ((v6 & 0x10) != 0)
                    {
                        newAttributes.Add(cell.AttrA3 ?? new TileAttrModel { Byte0 = 0, Byte1 = 0, Byte2 = 0, Byte3 = 0 });
                    }
                }

                _stage.MapTerrain.Tiles = newTiles;
                _stage.MapTerrain.Attributes = newAttributes;

                // Units
                if (_stage.AIInfo?.Agents != null)
                {
                    var keptAgents = new List<AIAgentModel>();
                    foreach (var agent in _stage.AIInfo.Agents)
                    {
                        if (!agent.AgentInfo.CellIdx.HasValue) continue;
                        if (agent.AgentInfo.CellIdx.Value == 65535)
                        {
                            keptAgents.Add(agent);
                            continue;
                        }
                        int x = agent.AgentInfo.CellIdx.Value % oldW;
                        int y = agent.AgentInfo.CellIdx.Value / oldW;
                        if (x < newW && y < newH)
                        {
                            agent.AgentInfo.CellIdx = (ushort)(y * newW + x);
                            keptAgents.Add(agent);
                        }
                    }
                    _stage.AIInfo.Agents = keptAgents;
                }

                // Events
                if (_stage.TriggerInfo?.Events != null)
                {
                    var keptEvents = new List<TriggerEventModel>();
                    foreach (var ev in _stage.TriggerInfo.Events)
                    {
                        int x = ev.TileIndex % oldW;
                        int y = ev.TileIndex / oldW;
                        if (x < newW && y < newH)
                        {
                            ev.TileIndex = (ushort)(y * newW + x);
                            keptEvents.Add(ev);
                        }
                    }
                    _stage.TriggerInfo.Events = keptEvents;
                }

                // Reinforcements
                if (_stage.BattleInfo?.ReinforcePoints != null)
                {
                    var keptRPs = new List<ReinforcePointModel>();
                    foreach (var rp in _stage.BattleInfo.ReinforcePoints)
                    {
                        if (!rp.CellIdx.HasValue) continue;
                        int x = rp.CellIdx.Value % oldW;
                        int y = rp.CellIdx.Value / oldW;
                        if (x < newW && y < newH)
                        {
                            rp.CellIdx = (ushort)(y * newW + x);
                            keptRPs.Add(rp);
                        }
                    }
                    _stage.BattleInfo.ReinforcePoints = keptRPs;
                }

                // Decals
                if (_stage.DecalInfo?.Decals != null)
                {
                    var keptDecals = new List<DecalModel>();
                    foreach (var dec in _stage.DecalInfo.Decals)
                    {
                        if (dec.X < newW && dec.Y < newH)
                        {
                            keptDecals.Add(dec);
                        }
                    }
                    _stage.DecalInfo.Decals = keptDecals;
                }

                // SubRegions
                if (_stage.RegionInfo?.SubRegions != null)
                {
                    foreach (var sub in _stage.RegionInfo.SubRegions)
                    {
                        if (sub.TileIndices != null)
                        {
                            var newIndices = new List<ushort>();
                            foreach (var idx in sub.TileIndices)
                            {
                                int x = idx % oldW;
                                int y = idx / oldW;
                                if (x < newW && y < newH)
                                {
                                    newIndices.Add((ushort)(y * newW + x));
                                }
                            }
                            sub.TileIndices = newIndices;
                        }
                    }
                }

                ushort left = (ushort)Math.Clamp((int)(nudLeftMargin.Value ?? _stage.MapTerrain.Size.LeftMargin), 0, newW);
                ushort top = (ushort)Math.Clamp((int)(nudTopMargin.Value ?? _stage.MapTerrain.Size.TopMargin), 0, newH);
                ushort maxW = (ushort)Math.Max(1, newW - left);
                ushort maxH = (ushort)Math.Max(1, newH - top);
                ushort playW = (ushort)Math.Clamp((int)(nudPlayWidth.Value ?? maxW), 1, maxW);
                ushort playH = (ushort)Math.Clamp((int)(nudPlayHeight.Value ?? maxH), 1, maxH);

                _stage.MapTerrain.Size.Width = newW;
                _stage.MapTerrain.Size.Height = newH;
                _stage.MapTerrain.Size.LeftMargin = left;
                _stage.MapTerrain.Size.TopMargin = top;
                _stage.MapTerrain.Size.PlayableWidth = playW;
                _stage.MapTerrain.Size.PlayableHeight = playH;

                _isUpdatingUi = true;
                nudLeftMargin.Value = left;
                nudTopMargin.Value = top;
                nudPlayWidth.Value = playW;
                nudPlayHeight.Value = playH;
                _isUpdatingUi = false;

                welcomeOverlay.IsVisible = false;
                mapCanvas.RebuildCells();
                mapCanvas.InvalidateMeasure();
                mapCanvas.InvalidateVisual();
                _selectedCellIdx = -1;

                lblCellCoords.Text = "坐标: 未选择";
                txtMapSize.Text = $"地图: {Path.GetFileName(_loadedFilePath)} ({newW}x{newH}) (已重设尺寸)";

                RefreshFactionList();
                RefreshReinforceList();
            }
            catch (Exception ex)
            {
                txtMapSize.Text = "调整尺寸失败！";
                Console.WriteLine($"[重调尺寸错误] {ex.Message}");
            }
        }

        private void OnPlayableBoundsChanged()
        {
            if (_isUpdatingUi || _stage?.MapTerrain?.Size == null) return;
            ushort w = _stage.MapTerrain.Size.Width;
            ushort h = _stage.MapTerrain.Size.Height;

            ushort left = (ushort)Math.Clamp((int)(nudLeftMargin.Value ?? 0), 0, w);
            ushort top = (ushort)Math.Clamp((int)(nudTopMargin.Value ?? 0), 0, h);
            ushort playW = (ushort)Math.Clamp((int)(nudPlayWidth.Value ?? (w - left)), 1, Math.Max(1, w - left));
            ushort playH = (ushort)Math.Clamp((int)(nudPlayHeight.Value ?? (h - top)), 1, Math.Max(1, h - top));

            _stage.MapTerrain.Size.LeftMargin = left;
            _stage.MapTerrain.Size.TopMargin = top;
            _stage.MapTerrain.Size.PlayableWidth = playW;
            _stage.MapTerrain.Size.PlayableHeight = playH;

            mapCanvas.InvalidateVisual();
        }

        private static int? GetBehaviorFieldNullable(Dictionary<string, object> ext, string classKey, string shortKey, string backupKey)
        {
            if (ext == null) return null;
            string key = SymbolManager.GetCustomName(classKey, shortKey);
            if (ext.TryGetValue(key, out var val) && val != null)
            {
                try { return Convert.ToInt32(val.ToString()); } catch {}
            }
            if (ext.TryGetValue(backupKey, out val) && val != null)
            {
                try { return Convert.ToInt32(val.ToString()); } catch {}
            }
            return null;
        }

        private static int? GetBehaviorFieldNullable(Dictionary<string, object> ext, string fieldName)
        {
            if (ext == null) return null;
            var keys = GetFieldKeys("AIAgentBehavior", fieldName);
            foreach (var k in keys)
            {
                if (ext.TryGetValue(k, out var val) && val != null)
                {
                    try { return Convert.ToInt32(val.ToString()); } catch {}
                }
            }
            return null;
        }

        private static object GetExtensionValue(Dictionary<string, object> ext, string key)
        {
            if (ext == null || !ext.TryGetValue(key, out var val)) return null;
            if (val is System.Text.Json.JsonElement je)
            {
                switch (je.ValueKind)
                {
                    case System.Text.Json.JsonValueKind.Number:
                        if (je.TryGetInt32(out int i)) return i;
                        if (je.TryGetDouble(out double d)) return d;
                        break;
                    case System.Text.Json.JsonValueKind.True: return true;
                    case System.Text.Json.JsonValueKind.False: return false;
                    case System.Text.Json.JsonValueKind.String: return je.GetString();
                }
            }
            return val;
        }

        private static int GetExtensionInt(Dictionary<string, object> ext, string key, int defaultVal = 0)
        {
            var val = GetExtensionValue(ext, key);
            if (val == null) return defaultVal;
            try { return Convert.ToInt32(val); } catch { return defaultVal; }
        }

        private static bool GetExtensionBool(Dictionary<string, object> ext, string key, bool defaultVal = false)
        {
            var val = GetExtensionValue(ext, key);
            if (val == null) return defaultVal;
            try { return Convert.ToBoolean(val); } catch { return defaultVal; }
        }

        private static Dictionary<string, object> GetExtensionDict(Dictionary<string, object> ext, string key)
        {
            if (ext == null || !ext.TryGetValue(key, out var val)) return null;
            if (val is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var dict = new Dictionary<string, object>();
                foreach (var prop in je.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value;
                }
                return dict;
            }
            return val as Dictionary<string, object>;
        }

        private static int GetResilientInt(Dictionary<string, object> dict, string section, string uniqueKey, string rawFieldName, string fallbackName, int defaultVal)
        {
            if (dict == null) return defaultVal;
            string customKey = SymbolManager.GetCustomName(section, uniqueKey);
            if (dict.ContainsKey(customKey)) return GetExtensionInt(dict, customKey, defaultVal);
            if (dict.ContainsKey(uniqueKey)) return GetExtensionInt(dict, uniqueKey, defaultVal);
            if (dict.ContainsKey(rawFieldName)) return GetExtensionInt(dict, rawFieldName, defaultVal);
            if (!string.IsNullOrEmpty(fallbackName) && dict.ContainsKey(fallbackName)) return GetExtensionInt(dict, fallbackName, defaultVal);
            foreach (var key in dict.Keys)
            {
                if (key.Equals(customKey, StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(uniqueKey, StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(rawFieldName, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(fallbackName) && key.Equals(fallbackName, StringComparison.OrdinalIgnoreCase)))
                {
                    return GetExtensionInt(dict, key, defaultVal);
                }
            }
            return defaultVal;
        }

        private static bool GetResilientBool(Dictionary<string, object> dict, string section, string uniqueKey, string rawFieldName, string fallbackName, bool defaultVal)
        {
            if (dict == null) return defaultVal;
            string customKey = SymbolManager.GetCustomName(section, uniqueKey);
            if (dict.ContainsKey(customKey)) return GetExtensionBool(dict, customKey, defaultVal);
            if (dict.ContainsKey(uniqueKey)) return GetExtensionBool(dict, uniqueKey, defaultVal);
            if (dict.ContainsKey(rawFieldName)) return GetExtensionBool(dict, rawFieldName, defaultVal);
            if (!string.IsNullOrEmpty(fallbackName) && dict.ContainsKey(fallbackName)) return GetExtensionBool(dict, fallbackName, defaultVal);
            foreach (var key in dict.Keys)
            {
                if (key.Equals(customKey, StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(uniqueKey, StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(rawFieldName, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(fallbackName) && key.Equals(fallbackName, StringComparison.OrdinalIgnoreCase)))
                {
                    return GetExtensionBool(dict, key, defaultVal);
                }
            }
            return defaultVal;
        }

        private string GetBaseTerrainName(int baseT)
        {
            if (baseT == 0) return "平原";
            if (baseT == 1) return "海洋";
            if (baseT == 2) return "沙漠";
            if (baseT == 3) return "雪地";
            if (baseT == 4) return "沼泽";
            if (baseT == 5) return "丘陵";
            if (baseT == 6) return "森林";
            if (baseT == 7) return "城市";
            return $"未知地质({baseT})";
        }

        private string GetDoodadName(int id)
        {
            if (id == 0) return "无";
            if (GameSettings.MapTerrains.TryGetValue(id, out var terrain))
                return terrain.Name;
            return $"D{id}";
        }
    
        private static List<string> GetFieldKeys(string tableName, string fieldName)
        {
            var keys = new List<string>();
            try
            {
                if (BtlToolchain.BtlSchema.FieldToUniqueName.TryGetValue(tableName, out var dict))
                {
                    if (dict.TryGetValue(fieldName, out var uniqueKey))
                    {
                        keys.Add(SymbolManager.GetCustomName(tableName, uniqueKey));
                        keys.Add(uniqueKey);
                    }
                }
            }
            catch {}
            keys.Add(fieldName);
            return keys.Distinct().ToList();
        }

        private static int? GetResilientIntNullable(Dictionary<string, object> dict, string section, string uniqueKey, string rawFieldName, string fallbackName)
        {
            if (dict == null) return null;
            string customKey = SymbolManager.GetCustomName(section, uniqueKey);
            if (dict.ContainsKey(customKey)) return GetExtensionIntNullable(dict, customKey);
            if (dict.ContainsKey(uniqueKey)) return GetExtensionIntNullable(dict, uniqueKey);
            if (dict.ContainsKey(rawFieldName)) return GetExtensionIntNullable(dict, rawFieldName);
            if (!string.IsNullOrEmpty(fallbackName) && dict.ContainsKey(fallbackName)) return GetExtensionIntNullable(dict, fallbackName);
            foreach (var key in dict.Keys)
            {
                if (key.Equals(customKey, StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(uniqueKey, StringComparison.OrdinalIgnoreCase) ||
                    key.Equals(rawFieldName, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(fallbackName) && key.Equals(fallbackName, StringComparison.OrdinalIgnoreCase)))
                {
                    return GetExtensionIntNullable(dict, key);
                }
            }
            return null;
        }

        private static int? GetExtensionIntNullable(Dictionary<string, object> ext, string key)
        {
            var val = GetExtensionValue(ext, key);
            if (val == null) return null;
            try { return Convert.ToInt32(val); } catch { return null; }
        }

        private static void SetExtensionValueOrRemove(Dictionary<string, object> ext, string key, object value)
        {
            if (ext == null) return;
            if (value == null)
            {
                ext.Remove(key);
            }
            else
            {
                ext[key] = value;
            }
        }

}

}
