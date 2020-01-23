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
using Nefarius.ViGEm.Client.Targets.Xbox360;

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

        private static Xbox360Report[] lastreports = new Xbox360Report[5];
        //yes
        //please help i do not know how xbox360report works so here's my shitty workaround
        const ushort DPADUp =    0b1;
        const ushort DPADDown =  0b10;
        const ushort DPADLeft =  0b100;
        const ushort DPADRight = 0b1000;
        const ushort ButtonOpt = 0b10000;
        const ushort ButtonShr = 0b100000;
        const ushort ButtonL3 =  0b1000000;
        const ushort ButtonR3 =  0b10000000;
        const ushort ButtonL1 =  0b100000000;
        const ushort ButtonR1 =  0b1000000000;
        const ushort ButtonPS =  0b10000000000;
        const ushort ButtonA =   0b1000000000000;
        const ushort ButtonB =   0b10000000000000;
        const ushort ButtonX =   0b100000000000000;
        const ushort ButtonY =   0b1000000000000000;

        const int PROCESS_WM_READ = 0x0010;

        public static volatile LinkType procHooked = LinkType.NotHooked;
        private static volatile bool isInLoop = false;

        private static volatile UInt32 refreshRate = 128;
        private static volatile UInt32 animLength = 500;
        private static volatile UInt32 animCounter = 0;

        private static volatile List<string> InitialScript = new List<string>();
        private static volatile List<string> LoopingScript = new List<string>();
        private static volatile List<string> ExecCondScript = new List<string>();
        private static volatile List<QWord> qwords = new List<QWord>();
        private static volatile List<DWord> dwords = new List<DWord>();
        private static volatile List<BWord> bwords = new List<BWord>();
        private static volatile List<FWord> fwords = new List<FWord>();
        private static volatile List<QWord> initialqwords = new List<QWord>();
        private static volatile List<DWord> initialdwords = new List<DWord>();
        private static volatile List<BWord> initialbwords = new List<BWord>();
        private static volatile List<FWord> initialfwords = new List<FWord>();

        private static List<List<string>> AllScripts = new List<List<string>>();

        private static DS4Color[] controllerColors = new DS4Color[5];

        private static List<ConnectedProc> hookedProcs = new List<ConnectedProc>();

        public enum LinkType {
            NotHooked = 0,
            ProcHooked = 1,
            ProcScanned = 2
        }

        public struct ConnectedProc {
            public IntPtr procPointer;
            public Process proc;
            public string procName;
        }
        public struct QWord
        {
            public UInt64 value;
            public string name;
        }
        public struct DWord
        {
            public UInt32 value;
            public string name;
        }
        public struct BWord    //bruh
        {
            public byte value;
            public string name;
        }
        public struct FWord
        {
            public float value;
            public string name;
        }

        private static Thread ScannerThread;
        private static Thread AnimUpdateThread;

        public static void GameEnd() {
            ScannerThread.Abort();
            AnimUpdateThread.Abort();
        }

        public static void Begin() {        //let's roll
            if (!Directory.Exists(Directory.GetCurrentDirectory() + "/LightbarScripts/"))
            {
                Directory.CreateDirectory(Directory.GetCurrentDirectory() + "/LightbarScripts/");
            }
            foreach (string a in Directory.GetFiles(Directory.GetCurrentDirectory() + "/LightbarScripts/"))
            {
                if (a.ToLower().EndsWith(".ds4lb"))
                {
                    AllScripts.Add(File.ReadLines(a).ToList<string>()); //i misspelled it the first time and accidentally wrote AssScripts                             
                }                                      //le funny programmer jonk haha
            }                                          //programming at 3 am is fun
            ScannerThread = new Thread(ScanManagerLoop);
            ScannerThread.Start();
            AnimUpdateThread = new Thread(UpdateAnim);
            AnimUpdateThread.Start();
        }

        public static void ScanManagerLoop() {
            while (true) {
                if ((Global.getUseScripts(0) || Global.getUseScripts(1) || Global.getUseScripts(2) || Global.getUseScripts(3)))
                {
                    for (int x = 0; x != hookedProcs.Count; x++) {
                        if (hookedProcs[x].proc != null)
                        {
                            if (hookedProcs[x].proc.HasExited)
                            {
                                hookedProcs.RemoveAt(x);
                                x--;
                            }
                        }
                        else {
                            if (Process.GetProcessesByName(hookedProcs[x].procName).Length == 0) {
                                if (procHooked == LinkType.ProcScanned)
                                {
                                    procHooked = LinkType.NotHooked;
                                }
                                hookedProcs.RemoveAt(x);
                                x--;
                            }
                        }
                    }
                    if (procHooked == LinkType.NotHooked)
                    {
                        scanAndHookOntoGame();
                        
                    }
                    if (procHooked == LinkType.ProcHooked && (process.HasExited || !CheckScriptConditions())) {
                        procHooked = LinkType.NotHooked;
                    }
                    

                }
                Thread.Sleep(2000);
            }
        }

        public static void UpdateAnim() {
            while (true)
            {
                animCounter += refreshRate;
                if (animCounter > 2 * animLength) {
                    animCounter -= 2 * animLength;
                }
                Thread.Sleep((int)refreshRate);
            }

        }

        public static void CleanVars() {
            qwords.Clear();
            dwords.Clear();
            bwords.Clear();
            fwords.Clear();
            initialqwords.Clear();
            initialdwords.Clear();
            initialbwords.Clear();
            initialfwords.Clear();
        }

        public static bool CheckScriptConditions() {
            return ExecUntilReturn(ExecCondScript, true, true, ref initialqwords, ref initialdwords, ref initialbwords, ref initialfwords, 0) != 0x00;
        }

        public static bool scanAndHookOntoGame() {

            foreach (List<string> a in AllScripts)
            {
                List<string> lines = a;
                foreach (string b in lines)
                {
                    if (b.StartsWith("hookproc"))
                    {
                        if (Process.GetProcessesByName(b.Split()[1]).Length != 0)
                        {
                            ConnectedProc c = new ConnectedProc();
                            bool found = false;
                            foreach (ConnectedProc d in hookedProcs)
                            {
                                if (d.procName == b.Split()[1])
                                {
                                    c = d;
                                    found = true;
                                    break;
                                }
                            }
                            if (!found)
                            {
                                Console.WriteLine("Process struct created");
                                c.proc = Process.GetProcessesByName(b.Split()[1])[0];
                                c.procName = b.Split()[1];
                                Console.WriteLine("Hooking onto process: scanAndHookOntoGame");
                                c.procPointer = OpenProcess(PROCESS_WM_READ, false, c.proc.Id);
                                hookedProcs.Add(c);
                            }
                            bool writingToExec = false;
                            bool writingToLoop = false;
                            List<string> inscr = new List<string>();
                            List<string> condscr = new List<string>();
                            List<string> loopscr = new List<string>();
                            foreach (string d in lines)
                            {
                                if (!d.StartsWith("//") && d != "")
                                {
                                    if (d == "execcond")
                                    {
                                        writingToExec = true;
                                        writingToLoop = false;
                                    }
                                    else if (d == "startloop")
                                    {
                                        writingToExec = false;
                                        writingToLoop = true;
                                    }
                                    if (!writingToLoop && !writingToExec)
                                    {
                                        inscr.Add(d);
                                    }
                                    else if (writingToLoop)
                                    {
                                        loopscr.Add(d);
                                    }
                                    else if (writingToExec)
                                    {
                                        condscr.Add(d);
                                    }

                                }
                            }
                            //i have no idea how to fix these warnings
                            //help
                            ExecUntilReturn(inscr, false, true, ref qwords, ref dwords, ref bwords, ref fwords, 0);
                            initialqwords = qwords.GetRange(0, qwords.Count);
                            initialdwords = dwords.GetRange(0, dwords.Count);
                            initialbwords = bwords.GetRange(0, bwords.Count);
                            initialfwords = fwords.GetRange(0, bwords.Count);
                            if (ExecUntilReturn(condscr, true, true, ref qwords, ref dwords, ref bwords, ref fwords, 0) != 0x00)
                            {
                                InitialScript = inscr;
                                LoopingScript = loopscr;
                                ExecCondScript = condscr;
                                procHooked = LinkType.ProcHooked;
                                return true;
                            }
                            else
                            {
                                DetachProcess();
                                CleanVars();
                            }
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else if (b.StartsWith("scanproc")) {
                        if (Process.GetProcessesByName(b.Split()[1]).Length != 0)
                        {
                            
                            Console.WriteLine("Process struct created");
                            ConnectedProc c = new ConnectedProc();
                            c.procName = b.Split()[1];
                            hookedProcs.Add(c);
                            bool writingToExec = false;
                            bool writingToLoop = false;
                            CleanVars();
                            List<string> inscr = new List<string>();
                            List<string> condscr = new List<string>();
                            List<string> loopscr = new List<string>();
                            foreach (string d in lines)
                            {
                                if (!d.StartsWith("//") && d != "")
                                {
                                    if (d == "execcond")
                                    {
                                        writingToExec = true;
                                        writingToLoop = false;
                                    }
                                    else if (d == "startloop")
                                    {
                                        writingToExec = false;
                                        writingToLoop = true;
                                    }
                                    if (!writingToLoop && !writingToExec)
                                    {
                                        inscr.Add(d);
                                    }
                                    else if (writingToLoop)
                                    {
                                        loopscr.Add(d);
                                    }
                                    else if (writingToExec)
                                    {
                                        condscr.Add(d);
                                    }

                                }
                            }
                            ExecUntilReturn(inscr, false, true, ref qwords, ref dwords, ref bwords, ref fwords, 0);
                            InitialScript = inscr;
                            LoopingScript = loopscr;
                            ExecCondScript = condscr;
                            procHooked = LinkType.ProcScanned;
                            return true;
                        }
                        break;
                    }
                }
            }
            return false;
        }

        public static void ReceiveControllerReport(Xbox360Report report, int device)
        {
            lastreports[device] = report;
        }

        public static UInt32 ExecUntilReturn(List<string> strlist, bool waitForReturn, bool testmode, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref List<FWord> flist, int DS4_ID)
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
                    case "scanproc":
                        //let's do nothing and hope it works
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
                    case "setf":
                        SetF(a[1], a[2], isInLoop, ref qlist, ref dlist, ref blist, ref flist, DS4_ID);
                        break;
                    case "addq":
                        AddQ(a[1], a[2], ref qlist, ref dlist, ref blist, DS4_ID);
                        break;
                    case "addd":
                        AddD(a[1], a[2], ref qlist, ref dlist, ref blist, DS4_ID);
                        break;
                    case "divf":
                        DivF(a[1], a[2], ref qlist, ref dlist, ref blist, ref flist, DS4_ID);
                        break;
                    case "modf":
                        ModF(a[1], a[2], isInLoop, ref qlist, ref dlist, ref blist, ref flist, DS4_ID);
                        break;
                    case "startloop":
                        isInLoop = true;
                        break;
                    case "execcond":
                        //yeah
                        break;
                    case "cond":
                        Cond(a[1], a[2], (a.Length >= 4 ? a[3] : "0x01"), ref qlist, ref dlist, ref blist, ref progcnt, DS4_ID);
                        break;
                    case "cnnq":
                        CnNQ(a[1], a[2], (a.Length >= 4 ? a[3] : "0x01"), ref qlist, ref dlist, ref blist, ref progcnt, DS4_ID);
                        break;
                    case "cngr":
                        CnGR(a[1], a[2], (a.Length >= 4 ? a[3] : "0x01"), ref qlist, ref dlist, ref blist, ref progcnt, DS4_ID);
                        break;
                    case "cngq":
                        CnGQ(a[1], a[2], (a.Length >= 4 ? a[3] : "0x01"), ref qlist, ref dlist, ref blist, ref progcnt, DS4_ID);
                        break;
                    case "cnls":
                        CnLS(a[1], a[2], (a.Length >= 4 ? a[3] : "0x01"), ref qlist, ref dlist, ref blist, ref progcnt, DS4_ID);
                        break;
                    case "cnlq":
                        CnLQ(a[1], a[2], (a.Length >= 4 ? a[3] : "0x01"), ref qlist, ref dlist, ref blist, ref progcnt, DS4_ID);
                        break;
                    case "cnbtn":
                        CnBTN(a[1], (a.Length >= 3 ? a[2] : "0x01"), ref qlist, ref dlist, ref blist, ref progcnt, DS4_ID);
                        break;
                    case "retn":
                        //progcnt = 0;
                        return getDwordValue(a[1], ref qlist, ref dlist, ref blist, DS4_ID);
                    case "retpt":
                        return GetColorPart(getDwordValue(a[1], ref qlist, ref dlist, ref blist, DS4_ID), getFwordValue(a[2], ref qlist, ref dlist, ref blist, ref flist, DS4_ID));
                    case "retf":
                        if (animCounter < animLength)
                        {
                            return getDwordValue(a[1], ref qlist, ref dlist, ref blist, DS4_ID);
                        }
                        else {
                            return getDwordValue("0x000000", ref qlist, ref dlist, ref blist, DS4_ID);
                        }
                    case "retfi":
                        return GetColorPart(getDwordValue(a[1], ref qlist, ref dlist, ref blist, DS4_ID), (float)(animCounter % animLength) / animLength);
                    case "retfo":
                        if (animCounter < animLength)
                        {
                            return GetColorPart(getDwordValue(a[1], ref qlist, ref dlist, ref blist, DS4_ID), (float)((animLength - animCounter) % animLength) / animLength);
                        }
                        else {
                            return GetColorPart(getDwordValue(a[1], ref qlist, ref dlist, ref blist, DS4_ID), (float)((animLength - (animCounter - animLength)) % animLength) / animLength);
                        }
                    case "retfio":
                        if (animCounter <= animLength)
                        {
                            return GetColorPart(getDwordValue(a[1], ref qlist, ref dlist, ref blist, DS4_ID), (float)animCounter / animLength);
                        }
                        else
                        {
                            return GetColorPart(getDwordValue(a[1], ref qlist, ref dlist, ref blist, DS4_ID), (float)(animLength - (animCounter - animLength)) / animLength);
                        }
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

        private static UInt32 GetColorPart(UInt32 color, float part) {
            byte[] bytes = BitConverter.GetBytes(color);
            return BitConverter.ToUInt32(new byte[] { (byte)(bytes[0] * part), (byte)(bytes[1] * part), (byte)(bytes[2] * part), 0x00}, 0);
        }

        public static DS4Color getColor(int playernumber) {
            if (procHooked == LinkType.ProcHooked || procHooked == LinkType.ProcScanned)
            {
                //This makes a copy: verified
                List<QWord> sandboxQwords = qwords.GetRange(0, qwords.Count);
                List<DWord> sandboxDwords = dwords.GetRange(0, dwords.Count);
                List<BWord> sandboxBwords = bwords.GetRange(0, bwords.Count);
                List<FWord> sandboxFwords = fwords.GetRange(0, fwords.Count);
                int DS4_ID = playernumber;
                byte[] rgb = BitConverter.GetBytes(ExecUntilReturn(LoopingScript, true, false, ref sandboxQwords, ref sandboxDwords, ref sandboxBwords, ref sandboxFwords, DS4_ID));
                //the following code is shit and slow
                foreach (QWord a in sandboxQwords) {
                    for (int x = 0; x != qwords.Count; x++) {
                        if (a.name == qwords[x].name) {
                            qwords[x] = a;
                            break;
                        }
                    }
                }
                foreach (DWord a in sandboxDwords)
                {
                    for (int x = 0; x != dwords.Count; x++)
                    {
                        if (a.name == dwords[x].name)
                        {
                            dwords[x] = a;
                            break;
                        }
                    }
                }
                foreach (BWord a in sandboxBwords)
                {
                    for (int x = 0; x != bwords.Count; x++)
                    {
                        if (a.name == bwords[x].name)
                        {
                            bwords[x] = a;
                            break;
                        }
                    }
                }
                foreach (FWord a in sandboxFwords)
                {
                    for (int x = 0; x != fwords.Count; x++)
                    {
                        if (a.name == fwords[x].name)
                        {
                            fwords[x] = a;
                            break;
                        }
                    }
                }
                return new DS4Color(rgb[2], rgb[1], rgb[0]);
            }
            return new DS4Color(255, 255, 255);
        }

        private static UInt64 getQwordValue(string name, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, int DS4_ID)
        {
            if (name == "BASE_ADDRESS")
            {
                if (procHooked == LinkType.ProcScanned)
                {
                    throw new ArgumentException("Cannot get base address in scanned process");
                }
                //Sometimes this throws a System.ComponentModel.Win32Exception
                //and i have no idea why
                return (UInt64)process.MainModule.BaseAddress;
            }
            else if (name == "DS4_PORT")
            {
                return (UInt64)DS4_ID;
            }
            else if (name == "LEN_ANIM")
            {
                return (UInt64)animLength;
            }
            else if (name == "REFRESH")
            {
                return (UInt64)refreshRate;
            }
            else if (name.StartsWith("0x"))
            {
                return UInt64.Parse(name.Substring(2), System.Globalization.NumberStyles.HexNumber);
            }
            else if (name.StartsWith("q:") || name.StartsWith("d:") || name.StartsWith("b:")) {
                return UInt64.Parse(name.Substring(2));
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
                if (procHooked == LinkType.ProcScanned)
                {
                    throw new ArgumentException("Cannot get base address in scanned process");
                }
                return (UInt32)process.MainModule.BaseAddress;
            }
            else if (name == "DS4_PORT")
            {
                return (UInt32)DS4_ID;
            }
            else if (name == "LEN_ANIM")
            {
                return (UInt32)animLength;
            }
            else if (name == "REFRESH") {
                return (UInt32)refreshRate;
            }
            else if (name.StartsWith("0x"))
            {
                return UInt32.Parse(name.Substring(2), System.Globalization.NumberStyles.HexNumber);
            }
            else if (name.StartsWith("d:") || name.StartsWith("b:"))
            {
                return UInt32.Parse(name.Substring(2));
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
            else if (name.StartsWith("b:"))
            {
                return byte.Parse(name.Substring(2));
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

        private static float getFwordValue(string name, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref List<FWord> flist, int DS4_ID) {
            if (name.StartsWith("f:"))
            {
                return float.Parse(name.Substring(2));
            }
            foreach (FWord a in flist)
            {
                if (a.name == name)
                {
                    return a.value;
                }
            }
            return getQwordValue(name, ref qlist, ref dlist, ref blist, DS4_ID);
            throw new ArgumentException("Value Error");
        }

        private static void DetachProcess() {
            if (procHooked == LinkType.ProcHooked) {
                processHandle = new IntPtr();
                procHooked = LinkType.NotHooked;
            }
        }

        private static void HookOntoProcess(string procname, bool testmode)
        {
            if (procHooked == LinkType.NotHooked)
            {
                while (Process.GetProcessesByName(procname).Length == 0)
                {
                    Thread.Sleep(500);
                }
                bool found = false;
                foreach (ConnectedProc a in hookedProcs) {
                    if (a.procName == procname) {
                        process = a.proc;
                        processHandle = a.procPointer;
                        found = true;
                    }
                }
                if (!found)
                {
                    process = Process.GetProcessesByName(procname)[0];
                    Console.WriteLine("Hooking onto process: HookOntoProcess");
                    processHandle = OpenProcess(PROCESS_WM_READ, false, process.Id);
                }
                if (!testmode)
                {
                    procHooked = LinkType.ProcHooked;
                }
            }
            else
            {
                throw new ArgumentException("Process hooked twice");
            }
        }

        private static void ModF(string name, string value, bool inLoop, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref List<FWord> flist, int DS4_ID) {
            SetF(name, "f:" + (getFwordValue(name, ref qlist, ref dlist, ref blist, ref flist, DS4_ID) % getFwordValue(value, ref qlist, ref dlist, ref blist, ref flist, DS4_ID)).ToString(), inLoop, ref qlist, ref dlist, ref blist, ref flist, DS4_ID);
        }

        private static bool SetF(string name, string value, bool inLoop, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref List<FWord> flist, int DS4_ID)
        {
            for (int idx = 0; idx != flist.Count; idx++)
            {
                if (flist[idx].name == name)
                {
                    FWord a = flist[idx];
                    a.value = getFwordValue(value, ref qlist, ref dlist, ref blist, ref flist, DS4_ID);
                    flist[idx] = a;
                    return true;
                }
            }
            FWord b = new FWord();
            b.name = name;
            b.value = getFwordValue(value, ref qlist, ref dlist, ref blist, ref flist, DS4_ID);
            flist.Add(b);
            return true;
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
            b.value = getQwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID);
            qlist.Add(b);
            return true;
        }
        private static bool SetD(string name, string value, bool inLoop, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, int DS4_ID)
        {
            if (name == "LEN_ANIM")
            {
                if (animLength != getDwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID))
                {
                    animCounter = 0;
                    animLength = getDwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID);
                }
                return true;
            }
            else if (name == "REFRESH") {
                refreshRate = getDwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID);
                return true;
            }
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
        private static void SubQ(string name, string value, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, int DS4_ID)
        {
            for (int idx = 0; idx != qlist.Count; idx++)
            {
                if (qlist[idx].name == name)
                {
                    QWord a = qlist[idx];
                    a.value -= getQwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID);
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
        private static void SubD(string name, string value, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, int DS4_ID)
        {
            for (int idx = 0; idx != dlist.Count; idx++)
            {
                if (dlist[idx].name == name)
                {
                    DWord a = dlist[idx];
                    a.value -= getDwordValue(value, ref qlist, ref dlist, ref blist, DS4_ID);
                    dlist[idx] = a;
                    break;
                }
            }
        }
        private static void DivF(string name, string value, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref List<FWord> flist, int DS4_ID)
        {
            for (int idx = 0; idx != flist.Count; idx++)
            {
                if (flist[idx].name == name)
                {
                    FWord a = flist[idx];
                    a.value /= getFwordValue(value, ref qlist, ref dlist, ref blist, ref flist, DS4_ID);
                    flist[idx] = a;
                    break;
                }
            }
        }
        private static void Cond(string value1, string value2, string valueSkip, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref int progcnt, int DS4_ID)
        {
            if (getQwordValue(value1, ref qlist, ref dlist, ref blist, DS4_ID) != getQwordValue(value2, ref qlist, ref dlist, ref blist, DS4_ID))
            {
                progcnt+= (int)getDwordValue(valueSkip, ref qlist, ref dlist, ref blist, DS4_ID);
            }
        }
        private static void CnNQ(string value1, string value2, string valueSkip, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref int progcnt, int DS4_ID)
        {
            if (getQwordValue(value1, ref qlist, ref dlist, ref blist, DS4_ID) == getQwordValue(value2, ref qlist, ref dlist, ref blist, DS4_ID))
            {
                progcnt += (int)getDwordValue(valueSkip, ref qlist, ref dlist, ref blist, DS4_ID);
            }
        }
        private static void CnGR(string value1, string value2, string valueSkip, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref int progcnt, int DS4_ID)
        {
            if (getQwordValue(value1, ref qlist, ref dlist, ref blist, DS4_ID) <= getQwordValue(value2, ref qlist, ref dlist, ref blist, DS4_ID))
            {
                progcnt += (int)getDwordValue(valueSkip, ref qlist, ref dlist, ref blist, DS4_ID);
            }
        }
        private static void CnGQ(string value1, string value2, string valueSkip, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref int progcnt, int DS4_ID)
        {
            if (getQwordValue(value1, ref qlist, ref dlist, ref blist, DS4_ID) < getQwordValue(value2, ref qlist, ref dlist, ref blist, DS4_ID))
            {
                progcnt += (int)getDwordValue(valueSkip, ref qlist, ref dlist, ref blist, DS4_ID);
            }
        }
        private static void CnLS(string value1, string value2, string valueSkip, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref int progcnt, int DS4_ID)
        {
            if (getQwordValue(value1, ref qlist, ref dlist, ref blist, DS4_ID) >= getQwordValue(value2, ref qlist, ref dlist, ref blist, DS4_ID))
            {
                progcnt += (int)getDwordValue(valueSkip, ref qlist, ref dlist, ref blist, DS4_ID);
            }
        }
        private static void CnLQ(string value1, string value2, string valueSkip, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref int progcnt, int DS4_ID)
        {
            if (getQwordValue(value1, ref qlist, ref dlist, ref blist, DS4_ID) > getQwordValue(value2, ref qlist, ref dlist, ref blist, DS4_ID))
            {
                progcnt += (int)getDwordValue(valueSkip, ref qlist, ref dlist, ref blist, DS4_ID);
            }
        }
        private static void CnBTN(string buttonvalue, string valueSkip, ref List<QWord> qlist, ref List<DWord> dlist, ref List<BWord> blist, ref int progcnt, int DS4_ID) {
            if (!GetButton(buttonvalue, DS4_ID)) {
                progcnt += (int)getDwordValue(valueSkip, ref qlist, ref dlist, ref blist, DS4_ID);
            }
        }
        public static bool GetButton(string value, int DS4_ID) {
            switch (value) {
                case "BTN_CROSS":
                    return (lastreports[DS4_ID].Buttons & ButtonA) != 0;
                case "BTN_CIRCLE":
                    return (lastreports[DS4_ID].Buttons & ButtonB) != 0;
                case "BTN_TRIANGLE":
                    return (lastreports[DS4_ID].Buttons & ButtonY) != 0;
                case "BTN_SQUARE":
                    return (lastreports[DS4_ID].Buttons & ButtonX) != 0;
                case "BTN_SHARE":
                    return (lastreports[DS4_ID].Buttons & ButtonShr) != 0;
                case "BTN_L1":
                    return (lastreports[DS4_ID].Buttons & ButtonL1) != 0;
                case "BTN_R1":
                    return (lastreports[DS4_ID].Buttons & ButtonR1) != 0;
                case "BTN_L3":
                    return (lastreports[DS4_ID].Buttons & ButtonL3) != 0;
                case "BTN_R3":
                    return (lastreports[DS4_ID].Buttons & ButtonR3) != 0;
                case "BTN_OPTIONS":
                    return (lastreports[DS4_ID].Buttons & ButtonOpt) != 0;
                case "DPAD_LEFT":
                    return (lastreports[DS4_ID].Buttons & DPADLeft) != 0;
                case "DPAD_UP":
                    return (lastreports[DS4_ID].Buttons & DPADUp) != 0;
                case "DPAD_DOWN":
                    return (lastreports[DS4_ID].Buttons & DPADDown) != 0;
                case "DPAD_RIGHT":
                    return (lastreports[DS4_ID].Buttons & DPADRight) != 0;
                default:
                    throw new ArgumentException("Invalid button ID");
            }
        }
    }
}
