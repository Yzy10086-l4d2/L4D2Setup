using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Installer
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int c);

        private static void ActivateExisting()
        {
            try
            {
                foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName("L4D2Setup"))
                {
                    p.Refresh();
                    IntPtr h = p.MainWindowHandle;
                    if (h == IntPtr.Zero) continue;
                    ShowWindow(h, 9);
                    SetForegroundWindow(h);
                    return;
                }
            }
            catch { }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string gameRoot = null;
            bool selfTest = false;
            bool uiTest = false;
            bool copyTest = false;
            string uiSize = "1280x900";


            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase)) selfTest = true;
                else if (string.Equals(a, "--uitest", StringComparison.OrdinalIgnoreCase)) uiTest = true;
                else if (string.Equals(a, "--copytest", StringComparison.OrdinalIgnoreCase)) copyTest = true;
                else if (a.StartsWith("--uitsize=", StringComparison.OrdinalIgnoreCase)) uiSize = a.Substring(10);

                else if (a.StartsWith("--game=", StringComparison.OrdinalIgnoreCase)) gameRoot = a.Substring(7).Trim('"');
            }

            // 单实例只约束界面模式：已经开着就把那个窗口叫到前面来
            bool created;
            Mutex only = new Mutex(true, "L4D2Setup_SingleInstance", out created);
            if (!created && !selfTest && !uiTest && !copyTest)
            {
                ActivateExisting();
                return 0;
            }

            Core.Init();
            if (selfTest) return Work.SelfTest(gameRoot);
            if (copyTest)
            {
                Core.ScanMods();
                System.Collections.Generic.List<ModItem> pick = new System.Collections.Generic.List<ModItem>();
                foreach (ModItem m in Core.ModList) { if (m.Size > 1000000 && m.Size < 60000000) { pick.Add(m); if (pick.Count >= 4) break; } }
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "copytest");
                if (System.IO.Directory.Exists(tmp)) System.IO.Directory.Delete(tmp, true);
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(tmp, "left4dead2\\addons"));
                int c1, s1; System.Collections.Generic.List<string> a1, f1;
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                Work.CopyMods(tmp, pick, null, out c1, out s1, out a1, out f1);
                sw.Stop();
                System.IO.File.WriteAllText(System.IO.Path.Combine(Core.LogDir, "copytest.txt"),
                    "第一遍: 新装 " + c1 + " 跳过 " + s1 + " 失败 " + f1.Count + " 用时 " + Math.Round(sw.Elapsed.TotalSeconds, 2) + " 秒\r\n" +
                    "落盘 " + System.IO.Directory.GetFiles(System.IO.Path.Combine(tmp, "left4dead2\\addons")).Length + " 个\r\n");
                int c2, s2; System.Collections.Generic.List<string> a2, f2;
                System.Diagnostics.Stopwatch sw2 = System.Diagnostics.Stopwatch.StartNew();
                Work.CopyMods(tmp, pick, null, out c2, out s2, out a2, out f2);
                sw2.Stop();
                System.IO.File.AppendAllText(System.IO.Path.Combine(Core.LogDir, "copytest.txt"),
                    "第二遍: 新装 " + c2 + " 跳过 " + s2 + " 失败 " + f2.Count + " 用时 " + Math.Round(sw2.Elapsed.TotalSeconds, 2) + " 秒\r\n");
                System.IO.Directory.Delete(tmp, true);
                return 0;
            }
            if (uiTest)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (MainForm f = new MainForm(gameRoot))
                {
                    string[] wh = uiSize.Split('x');
                    f.ClientSize = new System.Drawing.Size(int.Parse(wh[0]), int.Parse(wh[1]));
                    f.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                    f.Location = new System.Drawing.Point(-4000, -4000);
                    f.Show();
                    System.Windows.Forms.Application.DoEvents();
                    f.PerformLayout();
                    System.Windows.Forms.Application.DoEvents();
                    System.IO.File.WriteAllText(System.IO.Path.Combine(Core.LogDir, "uitest.txt"), f.LayoutDump(), new System.Text.UTF8Encoding(true));
                    using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                    {
                        f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height));
                        bmp.Save(System.IO.Path.Combine(Core.LogDir, "uitest.png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(gameRoot));
            return 0;
        }
    }
}
