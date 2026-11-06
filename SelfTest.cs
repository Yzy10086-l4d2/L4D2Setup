using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

// 装完自检：起一次游戏、看 crash_dumps\reports 里有没有新的 .dmp。
// 只依赖 .NET 基础库，不引用本程序其它类，方便单独编译测试。
public static class SelfTest
{
    public class Result
    {
        public string Kind;      // crash / clean / exited / busy / nosteam / nogame / noreport / launchfail
        public string Message;   // 直接给用户看的话
        public string Dump;      // 崩了的话：dmp 全路径
        public int Seconds;      // 这次自检用了多少秒
    }

    public static string ReportsDir(string root)
    {
        return Path.Combine(Path.Combine(Path.Combine(root, "left4dead2"), "crash_dumps"), "reports");
    }

    // 找出目录里最新的 dmp（没有就返回 null）
    private static string NewestDump(string dir)
    {
        string[] files;
        try { files = Directory.GetFiles(dir, "*.dmp"); }
        catch { return null; }
        if (files.Length == 0) return null;
        string best = files[0];
        DateTime bt = File.GetLastWriteTime(best);
        for (int i = 1; i < files.Length; i++)
        {
            DateTime t = File.GetLastWriteTime(files[i]);
            if (t > bt) { bt = t; best = files[i]; }
        }
        return best;
    }

    /// <summary>老入口：起游戏、看着、到点关掉。自检专用。</summary>
    public static Result Run(string root, int seconds, int modCount, Action<string> progress)
    {
        return Run(root, seconds, modCount, progress, false, null);
    }

    /// <summary>
    /// 起游戏并盯着崩溃记录。
    /// leaveRunning = true 时，撑过 seconds 没崩就当作「启动成功」，进程留着给人玩，
    /// 之后继续在后台盯 dump 到游戏退出（或 30 分钟上限），真崩了回调 onCrash。
    /// 这样「校验」和「启动游戏」就是一步，不用起两次。
    /// </summary>
    public static Result Run(string root, int seconds, int modCount, Action<string> progress,
                             bool leaveRunning, Action<Result> onCrash)
    {
        Result r = new Result();

        string exe = Path.Combine(root, "left4dead2.exe");
        if (!File.Exists(exe)) { r.Kind = "nogame"; r.Message = "找不到 left4dead2.exe，游戏目录可能选错了。"; return r; }

        string rep = ReportsDir(root);
        if (!Directory.Exists(rep))
        {
            r.Kind = "noreport";
            r.Message = "这个游戏目录里没有 crash_dumps\\reports 文件夹，自检跑不了（非 Steam 版？）。";
            return r;
        }
        if (Process.GetProcessesByName("left4dead2").Length > 0)
        {
            r.Kind = "busy";
            r.Message = "游戏正开着。先完全退出游戏，再跑自检。";
            return r;
        }
        if (Process.GetProcessesByName("steam").Length == 0)
        {
            r.Kind = "nosteam";
            r.Message = "Steam 没开着，游戏起不来。先打开 Steam，再点开始装或自检。";
            return r;
        }

        DateTime started = DateTime.Now;
        string before = NewestDump(rep);
        DateTime beforeTime = before == null ? DateTime.MinValue : File.GetLastWriteTime(before);

        Process p = null;
        try
        {
            ProcessStartInfo si = new ProcessStartInfo(exe, "-novid +map c2m1_highway");
            si.WorkingDirectory = root;
            si.UseShellExecute = false;
            p = Process.Start(si);
        }
        catch (Exception ex)
        {
            r.Kind = "launchfail";
            r.Message = "游戏起不来：" + ex.Message;
            return r;
        }

        bool crashed = false;
        string dump = null;
        int used = 0;
        while (true)
        {
            Thread.Sleep(2000);
            used = (int)(DateTime.Now - started).TotalSeconds;
            if (progress != null)
                progress((leaveRunning ? "游戏运行中：已跑 " : "自检中：已等 ") + used + " 秒…（崩了会自己出结果）");

            string cur = NewestDump(rep);
            if (cur != null && File.GetLastWriteTime(cur) > beforeTime) { crashed = true; dump = cur; break; }

            bool alive = false;
            try { alive = !p.HasExited; } catch { alive = false; }
            if (!alive)
            {
                // 进程没了再等 3 秒：崩溃转储有时会晚一点落盘
                Thread.Sleep(3000);
                cur = NewestDump(rep);
                if (cur != null && File.GetLastWriteTime(cur) > beforeTime) { crashed = true; dump = cur; }
                break;
            }
            if (used >= seconds) break;
        }
        used = (int)(DateTime.Now - started).TotalSeconds;
        // 只有「跑完就关」的老模式才杀进程；启动模式要留给人玩
        if (!leaveRunning) { try { if (!p.HasExited) { p.Kill(); p.WaitForExit(5000); } } catch { } }

        r.Seconds = used;
        if (crashed)
        {
            r.Kind = "crash";
            r.Dump = dump;
            r.Message = "自检没通过：游戏在第 " + used + " 秒崩了。\r\n"
                + "说明这次装/改的 " + modCount + " 个 MOD 里，至少有一个是坏的。\r\n\r\n"
                + "崩溃记录：" + Path.GetFileName(dump) + "\r\n\r\n"
                + "怎么缩小范围：下次只勾一半再装 + 自检。崩 → 坏的在那一半；不崩 → 坏的在另一半。\r\n"
                + "几轮就能揪出来，比一个个试快得多。";
        }
        else if (used >= seconds)
        {
            r.Kind = "clean";
            if (leaveRunning)
            {
                r.Message = "启动成功，玩了 " + used + " 秒没崩。\r\n"
                    + "游戏不用关，直接玩就行；后面的时间本工具还在后台盯着，真崩了会弹提示。";
                WatchAfterLaunch(p, rep, beforeTime, modCount, onCrash);
            }
            else
            {
                r.Message = "自检通过：" + seconds + " 秒没崩（游戏已自动关掉）。";
            }
        }
        else
        {
            r.Kind = "exited";
            r.Message = leaveRunning
                ? "游戏在第 " + used + " 秒退出了，没有崩溃记录（自己关的？）。"
                : "自检没结论：游戏在第 " + used + " 秒自己退出了，没有崩溃记录。\r\n"
                  + "可能是被手动关掉，或者启动参数有问题。想确认就在 Steam 里正常开一次看看。";
        }
        return r;
    }

    /// <summary>
    /// 游戏已经开着让人玩了，这里继续在后台盯 crash_dumps：
    /// 有新 dump 就立刻回调，游戏自己退了就安静收工。不碰游戏进程。
    /// </summary>
    private static void WatchAfterLaunch(Process p, string rep, DateTime beforeTime,
                                         int modCount, Action<Result> onCrash)
    {
        if (p == null) return;
        Thread t = new Thread(delegate()
        {
            DateTime until = DateTime.Now.AddMinutes(30);
            while (DateTime.Now < until)
            {
                Thread.Sleep(4000);
                bool alive;
                try { alive = !p.HasExited; }
                catch { alive = false; }

                string cur = null;
                try { cur = NewestDump(rep); }
                catch { }
                if (cur != null)
                {
                    DateTime t2 = DateTime.MinValue;
                    try { t2 = File.GetLastWriteTime(cur); }
                    catch { }
                    if (t2 > beforeTime)
                    {
                        Result r = new Result();
                        r.Kind = "crash";
                        r.Dump = cur;
                        r.Message = "游戏崩了（这次装/改的 " + modCount + " 个 MOD 里至少有一个坏的）。\r\n\r\n"
                            + "崩溃记录：" + Path.GetFileName(cur) + "\r\n\r\n"
                            + "下次只勾一半再装，就能把坏包缩到那一半里。";
                        if (onCrash != null) { try { onCrash(r); } catch { } }
                        return;
                    }
                }
                if (!alive) return;     // 正常退出，不用打扰
            }
        });
        t.IsBackground = true;
        t.Start();
    }
}
