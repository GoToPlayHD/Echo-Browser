using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Arbeitsspeicher eines Prozesses wie in der Spalte "Arbeitsspeicher" des Task-Managers: der private
    /// Working Set. Die einfache Summe der Working Sets würde gemeinsam genutzte Seiten (DLLs) mehrfach zählen.
    /// </summary>
    public static class ProcessMemoryReader
    {
        public static long PrivateWorkingSet(int processId)
        {
            IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    int status = NtQueryInformationProcess(handle, ProcessVmCounters, out VmCountersEx2 counters,
                        Marshal.SizeOf<VmCountersEx2>(), out _);
                    if (status == 0) return (long)counters.PrivateWorkingSetSize;
                }
                finally
                {
                    CloseHandle(handle);
                }
            }

            // Ältere Windows-Versionen: gesamter Working Set als Näherung
            try
            {
                using var process = Process.GetProcessById(processId);
                return process.WorkingSet64;
            }
            catch (ArgumentException)
            {
                return 0; // Prozess ist inzwischen beendet
            }
            catch (InvalidOperationException)
            {
                return 0;
            }
        }

        private const uint ProcessQueryLimitedInformation = 0x1000;
        private const int ProcessVmCounters = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct VmCountersEx2
        {
            public UIntPtr PeakVirtualSize;
            public UIntPtr VirtualSize;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
            public UIntPtr PrivateWorkingSetSize;
            public ulong SharedCommitUsage;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
            out VmCountersEx2 processInformation, int processInformationLength, out int returnLength);
    }
}
