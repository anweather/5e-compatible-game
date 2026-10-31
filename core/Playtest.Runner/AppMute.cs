// Mutes one process's sound in the Windows volume mixer (its audio session),
// for the playtest runner: the player still makes and hears its sound (the bot's
// audio checks read it before Windows), but nothing comes out of the speakers.
// Windows only.
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Playtest.Runner
{
    public static class AppMute
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int NotImpl1();
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            int NotImpl1();
            int NotImpl2();
            int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        }

        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            int GetCount(out int count);
            int GetSession(int index, out IAudioSessionControl2 session);
        }

        [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            int NotImpl0(); int NotImpl1(); int NotImpl2(); int NotImpl3(); int NotImpl4();
            int NotImpl5(); int NotImpl6(); int NotImpl7(); int NotImpl8();
            int GetSessionIdentifier(out IntPtr id);
            int GetSessionInstanceIdentifier(out IntPtr id);
            int GetProcessId(out uint pid);
        }

        [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISimpleAudioVolume
        {
            int SetMasterVolume(float level, ref Guid context);
            int GetMasterVolume(out float level);
            int SetMute(bool mute, ref Guid context);
            int GetMute(out bool mute);
        }

        /// <summary>What <see cref="Mute"/> returns when the PC has no default audio output: nothing can play aloud.</summary>
        public const int NoOutput = -1;

        /// <summary>Whether a <see cref="Mute"/> result means the player is silent: sessions muted, or no output to hear it on.</summary>
        public static bool Silenced(int result) => result > 0 || result == NoOutput;

        /// <summary>
        /// Mutes every audio session of a process on the default output; how many it muted (0 if it
        /// has none yet), or <see cref="NoOutput"/> when there is no default output (a PC with no audio
        /// device, or a VM: Windows answers "Element not found").
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static int Mute(int pid)
        {
            try
            {
                return MuteOn(pid);
            }
            catch (COMException)
            {
                return NoOutput;
            }
        }

        [SupportedOSPlatform("windows")]
        static int MuteOn(int pid)
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out IMMDevice device) != 0 || device == null) return NoOutput;
            var iid = typeof(IAudioSessionManager2).GUID;
            object o;
            device.Activate(ref iid, 23, IntPtr.Zero, out o);
            IAudioSessionEnumerator sessions;
            ((IAudioSessionManager2)o).GetSessionEnumerator(out sessions);
            int count, muted = 0;
            sessions.GetCount(out count);
            for (int i = 0; i < count; i++)
            {
                IAudioSessionControl2 s;
                sessions.GetSession(i, out s);
                uint p;
                s.GetProcessId(out p);
                if (p != pid) continue;
                var none = Guid.Empty;
                ((ISimpleAudioVolume)s).SetMute(true, ref none);
                bool now;
                ((ISimpleAudioVolume)s).GetMute(out now);
                if (now) muted++;
            }
            return muted;
        }
    }
}
