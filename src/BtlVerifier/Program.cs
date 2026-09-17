/*
 * Program.cs
 * 
 * 本文件是 BTL Verifier (BTL 校验与结构分析器) 的程序入口点：
 * - 命令行参数支持: 
 *   - `--test`: 运行批量解析测试，输出阵营势力元数据汇总文件。
 *   - `--verify-all`: 对游戏包体下所有 stage/conquest/corps 关卡 BTL 进行批量模拟加载崩溃检测。
 *   - `--verify <file>`: 针对单个 BTL 文件执行模拟加载并打印日志。
 *   - `--dump <file> <output>`: 导出指定二进制 BTL 文件的 IDA 风格反汇编文本布局。
 * - GUI 启动: 默认无参数运行时，初始化 WinForms 环境并打开 MainForm 交互式主分析窗口。
 */
using System;
using System.IO;
using System.Windows.Forms;

namespace BtlVerifier
{
    static class Program
    {
        /// <summary>
        ///  应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--test")
            {
                RunComparisonTest();
                return;
            }
            if (args.Length > 0 && args[0] == "--verify-all")
            {
                RunBatchVerification();
                return;
            }
            if (args.Length > 1 && args[0] == "--verify")
            {
                RunSingleVerification(args[1]);
                return;
            }
            if (args.Length > 1 && args[0] == "--dump")
            {
                string btlPath = args[1];
                string outputPath = args.Length > 2 ? args[2] : "dump.txt";
                RunDumpDisasm(btlPath, outputPath);
                return;
            }

            ApplicationConfiguration.Initialize();
            GameSettings.LoadDatabases();
            Application.Run(new MainForm());
        }

        static void RunSingleVerification(string btlPath)
        {
            try
            {
                BtlSchema.AutoLoadDefaultFbs();
                GameSettings.LoadDatabases();
                if (!System.IO.File.Exists(btlPath))
                {
                    Console.WriteLine($"Error: File not found: {btlPath}");
                    return;
                }
                var stage = BtlBridge.LoadBtl(btlPath);
                var logs = BtlSimLoader.Simulate(stage, out bool hasCrash);
                Console.WriteLine($"Verification result for {btlPath}: {(hasCrash ? "FAILED" : "PASSED")}");
                foreach (var log in logs)
                {
                    Console.WriteLine($"[{log.Type,-5}] [{log.Component,-13}] {log.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EXCEPTION during verification: {ex.ToString()}");
            }
        }

        static void RunDumpDisasm(string btlPath, string outputPath)
        {
            try
            {
                BtlSchema.AutoLoadDefaultFbs();
                byte[] data = System.IO.File.ReadAllBytes(btlPath);
                var stage = BtlBridge.LoadBtl(btlPath);
                var items = BtlBridge.GenerateDisasm(data, stage);
                
                using (var writer = new System.IO.StreamWriter(outputPath, false, System.Text.Encoding.UTF8))
                {
                    foreach (var item in items)
                    {
                        string offsetStr = item.Offset.ToString("X8");
                        string hexStr = "";
                        if (item.Length > 0 && item.Offset + item.Length <= data.Length)
                        {
                            var bytes = new byte[item.Length];
                            Array.Copy(data, item.Offset, bytes, 0, item.Length);
                            hexStr = BitConverter.ToString(bytes).Replace("-", " ");
                        }
                        writer.WriteLine($".disasm  :{offsetStr}    {hexStr,-34} {item.Description}");
                    }
                }
                Console.WriteLine($"[OK] Dumped disassembly to {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static void RunComparisonTest()
        {
            try
            {
                BtlSchema.AutoLoadDefaultFbs();
                string stageFolder = Path.Combine(BtlSchema.WorkspacePath, "游戏包体", "resources", "assets", "stage");
                if (!System.IO.Directory.Exists(stageFolder))
                {
                    Console.WriteLine($"Error: Stage folder not found: {stageFolder}");
                    return;
                }

                string[] btlFiles = System.IO.Directory.GetFiles(stageFolder, "stage*.btl");
                Console.WriteLine($"Found {btlFiles.Length} btl files. Parsing faction metadata...");

                string statsPath = Path.Combine(BtlSchema.WorkspacePath, "all_factions_stats.txt");
                using (var writer = new System.IO.StreamWriter(statsPath))
                {
                    writer.WriteLine("Stage,FactionIdx,FactionId,CountryId,Camp,IsAI,GeneralLimit,Gold,Tech");
                    foreach (var file in btlFiles)
                    {
                        string name = System.IO.Path.GetFileNameWithoutExtension(file);
                        try
                        {
                            var stage = BtlBridge.LoadBtl(file);
                            if (stage?.FactionInfo?.Factions != null)
                            {
                                for (int i = 0; i < stage.FactionInfo.Factions.Count; i++)
                                {
                                    var f = stage.FactionInfo.Factions[i];
                                    writer.WriteLine($"{name},{i},{f.Info.FactionId},{f.Info.CountryId},{f.Info.Camp},{f.Info.IsAI},{f.Info.GeneralLimit},{f.Info.InitialGold},{f.Info.InitialTech}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            writer.WriteLine($"{name},ERROR,{ex.Message}");
                        }
                    }
                }
                Console.WriteLine("[OK] Dumped all factions stats to " + statsPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static void RunBatchVerification()
        {
            try
            {
                BtlSchema.AutoLoadDefaultFbs();
                GameSettings.LoadDatabases();
                string stageFolder = Path.Combine(BtlSchema.WorkspacePath, "游戏包体", "resources", "assets", "stage");
                string outputDir = Path.Combine(BtlSchema.WorkspacePath, "调试器往返实验数据存放区");
                
                if (!System.IO.Directory.Exists(stageFolder))
                {
                    Console.WriteLine($"Error: Stage folder not found: {stageFolder}");
                    return;
                }
                if (!System.IO.Directory.Exists(outputDir))
                {
                    System.IO.Directory.CreateDirectory(outputDir);
                }

                var btlFilesList = new System.Collections.Generic.List<string>();
                btlFilesList.AddRange(System.IO.Directory.GetFiles(stageFolder, "stage1*.btl"));
                btlFilesList.AddRange(System.IO.Directory.GetFiles(stageFolder, "stage2*.btl"));
                btlFilesList.AddRange(System.IO.Directory.GetFiles(stageFolder, "stage3*.btl"));
                btlFilesList.AddRange(System.IO.Directory.GetFiles(stageFolder, "conquest*.btl"));
                btlFilesList.AddRange(System.IO.Directory.GetFiles(stageFolder, "corps*.btl"));
                string[] btlFiles = btlFilesList.ToArray();

                Console.WriteLine($"Found {btlFiles.Length} stage/conquest/corps BTL files to verify...");

                int total = btlFiles.Length;
                int passed = 0;
                int failed = 0;

                using (var reportWriter = new System.IO.StreamWriter(System.IO.Path.Combine(outputDir, "verify_report.txt"), false, System.Text.Encoding.UTF8))
                {
                    reportWriter.WriteLine("================================================================================");
                    reportWriter.WriteLine($" BTL batch verification report (stage/conquest/corps)");
                    reportWriter.WriteLine($" Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    reportWriter.WriteLine("================================================================================");
                    reportWriter.WriteLine();

                    foreach (var file in btlFiles)
                    {
                        string name = System.IO.Path.GetFileName(file);
                        try
                        {
                            var stage = BtlBridge.LoadBtl(file);
                            var logs = BtlSimLoader.Simulate(stage, out bool hasCrash);

                            if (hasCrash)
                            {
                                failed++;
                                reportWriter.WriteLine($"[-] {name,-18} : FAILED (Simulator detected potential crash!)");
                                
                                // Write detailed error log for this file
                                string detailPath = System.IO.Path.Combine(outputDir, $"{name}_error.log");
                                using (var detailWriter = new System.IO.StreamWriter(detailPath, false, System.Text.Encoding.UTF8))
                                {
                                    detailWriter.WriteLine($"File: {name}");
                                    detailWriter.WriteLine($"Result: FAILED");
                                    detailWriter.WriteLine("--------------------------------------------------------------------------------");
                                    foreach (var log in logs)
                                    {
                                        detailWriter.WriteLine($"[{log.Type,-5}] [{log.Component,-13}] {log.Message}");
                                    }
                                }
                            }
                            else
                            {
                                passed++;
                                reportWriter.WriteLine($"[+] {name,-18} : PASSED");
                            }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            reportWriter.WriteLine($"[-] {name,-18} : EXCEPTION ({ex.Message})");
                            
                            string detailPath = System.IO.Path.Combine(outputDir, $"{name}_error.log");
                            System.IO.File.WriteAllText(detailPath, $"File: {name}\nException:\n{ex.ToString()}", System.Text.Encoding.UTF8);
                        }
                    }

                    reportWriter.WriteLine();
                    reportWriter.WriteLine("================================================================================");
                    reportWriter.WriteLine($"Total files verified: {total}");
                    reportWriter.WriteLine($"PASSED              : {passed}");
                    reportWriter.WriteLine($"FAILED/CRASH RISK   : {failed}");
                    reportWriter.WriteLine("================================================================================");
                }

                Console.WriteLine($"[OK] Verified {total} files. PASSED: {passed}, FAILED: {failed}.");
                Console.WriteLine("Report saved to " + System.IO.Path.Combine(outputDir, "verify_report.txt"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
