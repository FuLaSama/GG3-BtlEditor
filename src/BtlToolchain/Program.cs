/*
 * Program.cs
 * 
 * 本文件是 BTL 转换独立工具的程序主入口点，初始化 WinForms 配置并运行 GuiForm 主窗口。
 */
using System;
using System.Windows.Forms;

namespace BtlToolchain
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new GuiForm());
        }
    }
}
