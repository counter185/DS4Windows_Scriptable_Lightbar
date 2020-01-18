using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DS4Windows;


namespace DS4WinWPF
{
    public static class ProcessLinker
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(int hProcess, Int64 lpBaseAddress, byte[] lpBuffer, int dwSize, ref int lpNumberOfBytesRead);

        private static Process process;
        private static IntPtr processHandle;


        const int PROCESS_WM_READ = 0x0010;

        public static volatile bool procHooked = false;
        private static volatile bool isInLoop = false;
        private static volatile int execsRunning = 0;

        private static volatile List<string> InitialScript = new List<string>();
        private static volatile List<string> LoopingScript = new List<string>();
        private static volatile List<string> ExecCondScript = new List<string>();
        private static volatile List<QWord> qwords = new List<QWord>();
        private static volatile List<DWord> dwords = new List<DWord>();
        private static volatile List<BWord> bwords = new List<BWord>();


        private static DS4Color[] controllerColors = new DS4Color[5];
        public struct QWord
        {
            public UInt64 value;
            public string name;
            public bool createdInLoop;
        }
        public struct DWord
        {
            public UInt32 value;
            public string name;
            public bool createdInLoop;
        }
        public struct BWord    //bruh
        {
            public byte value;
            public string name;
            public bool createdInLoop;
        }

        public static void Begin() {
            Console.WriteLine(scanAndHookOntoGame());
            new Thread(ScanManagerLoop).Start();
        }

        public static void ScanManagerLoop() {
            while (true) {
                if ((Global.getUseScripts(0) || Global.getUseScripts(1) || Global.getUseScripts(2) || Global.getUseScripts(3)))
                {
                    if (!procHooked)
                    {
                        scanAndHookOntoGame();
                        
                    }
                    if (procHooked && process.HasExited) {
                        procHooked = false;
                    }
                    Thread.Sleep(2000);

                }
                else {
                    Thread.Sleep(5000);
                }
            }
        }

        public static void CleanVars() {
            qwords.Clear();
            dwords.Clear();
            bwords.Clear();
        }

        public static bool scanAndHookOntoGame() {
            foreach (string a in Directory.GetFiles(Directory.GetCurrentDirectory())) {
                if (a.ToLower().EndsWith(".ds4lb"))
                {
                    List<string> lines = File.ReadLines(a).ToList<string>();
                    bool found = false;
                    foreach (string b in lines)
                    {
                        if (b.StartsWith("hookproc"))
                        {
                            if (Process.GetProcessesByName(b.Split()[1]).Length != 0)
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                    if (found)
                    {
                        bool writingToExec = false;
                        bool writingToLoop = false;
                        List<string> inscr = new List<string>();
                        List<string> condscr = new List<string>();
                        List<string> loopscr = new List<string>();
                        foreach (string b in lines)
                        {
                            if (!b.StartsWith("//") && b != "")
                            {
                                if (b == "execcond")
                                {
                                    writingToExec = true;
                                    writingToLoop = false;
                                }
                                else if (b == "startloop")
                                {
                                    writingToExec = false;
                                    writingToLoop = true;
                                }
                                if (!writingToLoop && !writingToExec)
                                {
                                    inscr.Add(b);
                                }
                                else if (writingToLoop)
                                {
                                    loopscr.Add(b);
                                }
                                else if (writingToExec)
                                {
                                    condscr.Add(b);
                                }
                                
                            }
                        }
                        ExecUntilReturn(inscr, false, true, ref qwords, ref dwords, ref bwords, 0);
                        if (ExecUntilReturn(condscr, true, true, ref qwords, ref dwords, ref bwords, 0) != 0x00)
                        {
                            procHooked = true;
                            InitialScript = inscr;
                            LoopingScript = loopscr;
                            ExecCondScript = condscr;
                            return true;
                        }
                        else {
                            DetachProcess();
                            CleanVars();
                        }
                    }
                }
            }
            return false;
        }

        public static UInt32 ExecUntilReturn(List<string> strlist, bool waitForReturn, bool testmode, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, int DS4_ID)
        {
            int progcnt = 0;
            while (true)
            {
                string[] a = strlist[progcnt].Split(" ".ToCharArray());
                switch (a[0])
                {
                    case "hookproc":
                        HookOntoProcess(a[1], testmode);
                        break;
                    case "setq":
                        SetQ(a[1], a[2], isInLoop, ref qlist, ref dlist, ref blist, DS4_ID);
                        break;
                    case "setd":
                        SetD(a[1], a[2], isInLoop, ref qlist, ref dlist, ref blist, DS4_ID);
                        break;
                    case "setb":
                        SetB(a[1], a[2], isInLoop, ref qlist, ref dlist, ref blist, DS4_ID);
                        break;
                    case "addq":
                        AddQ(a[1], a[2], ref qlist, ref dlist, ref blist, DS4_ID);
                        break;
                    case "addd":
                        AddD(a[1], a[2], ref qlist, ref dlist, ref blist, DS4_ID);
                        break;
                    case "startloop":
                        isInLoop = true;
                        break;
                    case "execcond":
                        //yeah
                        break;
                    case "cond":
                        Cond(a[1], a[2], ref qlist, ref dlist, ref blist, ref progcnt, DS4_ID);
                        break;
                    case "cngr":
                        CnGR(a[1], a[2], ref qlist, ref dlist, ref blist, ref progcnt, DS4_ID);
                        break;
                    case "cngq":
                        CnGQ(a[1], a[2], ref qlist, ref dlist, ref blist, ref progcnt, DS4_ID);
                        break;
                    case "cnls":
                        CnLS(a[1], a[2], ref qlist, ref dlist, ref blist, ref progcnt, DS4_ID);
                        break;
                    case "cnlq":
                        CnLQ(a[1], a[2], ref qlist, ref dlist, ref blist, ref progcnt, DS4_ID);
                        break;
                    case "retn":
                        //progcnt = 0;
                        return getDwordValue(a[1], ref qlist, ref dlist, ref blist, DS4_ID);
                    default:
                        throw new ArgumentException("Invalid instruction: " + a[0]);
                }
                progcnt++;
                if (progcnt >= strlist.Count)
                {
                    if (waitForReturn)
                    {
                        throw new ArgumentException("Script reached End of File without return");
                    }
                    else
                    {
                        return 0x00;
                    }
                }
            }
            
        }

        public static DS4Color getColor(int playernumber) {
            if (procHooked)
            {
                //This is a copy: verified
                List<QWord> sandboxQwords = qwords.GetRange(0, qwords.Count);
                List<DWord> sandboxDwords = dwords.GetRange(0, dwords.Count);
                List<BWord> sandboxBwords = bwords.GetRange(0, bwords.Count);
                int DS4_ID = playernumber;
                byte[] rgb = BitConverter.GetBytes(ExecUntilReturn(LoopingScript, true, false, ref sandboxQwords, ref sandboxDwords, ref sandboxBwords, DS4_ID));
                return new DS4Color(rgb[2], rgb[1], rgb[0]);
            }
            return new DS4Color(255, 255, 255);
        }

        private static UInt64 getQwordValue(string name, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, int DS4_ID)
        {
            if (name == "BASE_ADDRESS")
            {
                return (UInt64)process.MainModule.BaseAddress;
            }
            else if (name == "DS4_PORT")
            {
                return (UInt64)DS4_ID;
            }
            else if (name.StartsWith("0x"))
            {
                return UInt64.Parse(name.Substring(2), System.Globalization.NumberStyles.HexNumber);
            }
            else if (name.StartsWith("mem:"))
            {
                byte[] buffer = new byte[8];
                int bytesRead = 0;
                ReadProcessMemory((int)processHandle, (Int64)getQwordValue(name.Substring(4), ref qlist, ref dlist, ref blist, DS4_ID), buffer, buffer.Length, ref bytesRead);
                return BitConverter.ToUInt64(buffer, 0);
            }
            foreach (QWord a in qlist)
            {
                if (a.name == name)
                {
                    return a.value;
                }
            }
            foreach (DWord a in dlist)
            {
                if (a.name == name)
                {
                    return a.value;
                }
            }
            foreach (BWord a in blist)
            {
                if (a.name == name)
                {
                    return a.value;
                }
            }
            throw new ArgumentException("Value Error: " + name);
        }

        private static UInt32 getDwordValue(string name, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, int DS4_ID)
        {
            if (name == "BASE_ADDRESS")
            {
                return (UInt32)process.MainModule.BaseAddress;
            }
            else if (name == "DS4_PORT")
            {
                return (UInt32)DS4_ID;
            }
            else if (name.StartsWith("0x"))
            {
                return UInt32.Parse(name.Substring(2), System.Globalization.NumberStyles.HexNumber);
            }
            else if (name.StartsWith("mem:"))
            {
                byte[] buffer = new byte[4];
                int bytesRead = 0;
                ReadProcessMemory((int)processHandle, (Int64)getQwordValue(name.Substring(4), ref qlist, ref dlist, ref blist, DS4_ID), buffer, buffer.Length, ref bytesRead);
                return BitConverter.ToUInt32(buffer, 0);
            }
            foreach (DWord a in dlist)
            {
                if (a.name == name)
                {
                    return a.value;
                }
            }
            foreach (BWord a in blist)
            {
                if (a.name == name)
                {
                    return a.value;
                }
            }
            throw new ArgumentException("Value Error");
        }

        private static byte getBwordValue(string name, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, int DS4_ID)
        {
            if (name == "BASE_ADDRESS")
            {
                throw new ArgumentException("Value Error: BASE_ADDRESS cannot be parsed to byte");
            }
            else if (name == "DS4_PORT")
            {
                throw new ArgumentException("Value Error: DS4_PORT cannot be parsed to byte");
            }
            else if (name.StartsWith("0x"))
            {
                return byte.Parse(name.Substring(2), System.Globalization.NumberStyles.HexNumber);
            }
            else if (name.StartsWith("mem:"))
            {
                byte[] buffer = new byte[1];
                int bytesRead = 0;
                ReadProcessMemory((int)processHandle, (Int64)getQwordValue(name.Substring(4), ref qlist, ref dlist, ref blist, DS4_ID), buffer, buffer.Length, ref bytesRead);
                return buffer[0];
            }
            foreach (BWord a in blist)
            {
                if (a.name == name)
                {
                    return a.value;
                }
            }
            throw new ArgumentException("Value Error");
        }

        private static void DetachProcess() {
            if (procHooked) {
                process.Dispose();
                processHandle = new IntPtr();
                procHooked = false;
            }
        }

        private static void HookOntoProcess(string procname, bool testmode)
        {
            DetachProcess();
            if (!procHooked)
            {
                while (Process.GetProcessesByName(procname).Length == 0)
                {
                    Thread.Sleep(500);
                }
                process = Process.GetProcessesByName(procname)[0];
                processHandle = OpenProcess(PROCESS_WM_READ, false, process.Id);
                if (!testmode)
                {
                    procHooked = true;
                }
            }
            else
            {
                throw new ArgumentException("Process hooked twice");
            }
        }

        private static bool SetQ(string name, string value, bool inLoop, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, int DS4_ID)
        {
            for (int idx = 0; idx != qlist.Count; idx++)
            {
                if (qlist[idx].name == name)
                {
                    QWord a = qlist[idx];
                    a.value = getQwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID);
                    qlist[idx] = a;
                    return true;
                }
            }
            QWord b = new QWord();
            b.name = name;
            b.createdInLoop = inLoop;
            b.value = getQwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID);
            qlist.Add(b);
            return true;
        }
        private static bool SetD(string name, string value, bool inLoop, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, int DS4_ID)
        {
            for (int idx = 0; idx != dlist.Count; idx++)
            {
                if (dlist[idx].name == name)
                {
                    DWord a = dlist[idx];
                    a.value = getDwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID);
                    dlist[idx] = a;
                    return true;
                }
            }
            DWord b = new DWord();
            b.name = name;
            b.createdInLoop = inLoop;
            b.value = getDwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID);
            dlist.Add(b);
            return true;
        }
        private static bool SetB(string name, string value, bool inLoop, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, int DS4_ID)
        {
            for (int idx = 0; idx != blist.Count; idx++)
            {
                if (blist[idx].name == name)
                {
                    BWord a = blist[idx];
                    a.value = getBwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID);
                    blist[idx] = a;
                    return true;
                }
            }
            BWord b = new BWord();
            b.name = name;
            b.createdInLoop = inLoop;
            b.value = getBwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID);
            blist.Add(b);
            return true;
        }

        private static void AddQ(string name, string value, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, int DS4_ID)
        {
            for (int idx = 0; idx != qlist.Count; idx++)
            {
                if (qlist[idx].name == name)
                {
                    QWord a = qlist[idx];
                    a.value += getQwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID);
                    qlist[idx] = a;
                    break;
                }
            }
        }
        private static void AddD(string name, string value, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, int DS4_ID)
        {
            for (int idx = 0; idx != dlist.Count; idx++)
            {
                if (dlist[idx].name == name)
                {
                    DWord a = dlist[idx];
                    a.value += getDwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID);
                    dlist[idx] = a;
                    break;
                }
            }
        }
        private static void Cond(string value1, string value2, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref int progcnt, int DS4_ID)
        {
            if (getQwordValue(value1, ref qlist, ref dlist, ref blist, DS4_ID) != getQwordValue(value2, ref qlist, ref dlist, ref blist, DS4_ID))
            {
                progcnt++;
            }
        }
        private static void CnGR(string value1, string value2, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref int progcnt, int DS4_ID)
        {
            if (getQwordValue(value1, ref qlist, ref dlist, ref blist, DS4_ID) <= getQwordValue(value2, ref qlist, ref dlist, ref blist, DS4_ID))
            {
                progcnt++;
            }
        }
        private static void CnGQ(string value1, string value2, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref int progcnt, int DS4_ID)
        {
            if (getQwordValue(value1, ref qlist, ref dlist, ref blist, DS4_ID) < getQwordValue(value2, ref qlist, ref dlist, ref blist, DS4_ID))
            {
                progcnt++;
            }
        }
        private static void CnLS(string value1, string value2, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref int progcnt, int DS4_ID)
        {
            if (getQwordValue(value1, ref qlist, ref dlist, ref blist, DS4_ID) >= getQwordValue(value2, ref qlist, ref dlist, ref blist, DS4_ID))
            {
                progcnt++;
            }
        }
        private static void CnLQ(string value1, string value2, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref int progcnt, int DS4_ID)
        {
            if (getQwordValue(value1, ref qlist, ref dlist, ref blist, DS4_ID) > getQwordValue(value2, ref qlist, ref dlist, ref blist, DS4_ID))
            {
                progcnt++;
            }
        }
    }
}
