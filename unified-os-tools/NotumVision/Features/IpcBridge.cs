using System;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Ascension.Features
{
    // Super-lightweight NamedPipe client: sends "PAUSE"/"RESUME"/"PING" to a cooperating plugin.
    public class IpcBridge
    {
        public bool Enabled;
        public string PipeName = "Ascension.Stacker";
        public int TimeoutMs = 25;

        private readonly Action<string> _info;
        private readonly Action<string> _warn;

        public IpcBridge(Action<string> info, Action<string> warn)
        {
            _info = info ?? (_ => { });
            _warn = warn ?? (_ => { });
        }

        public bool TrySend(string payload)
        {
            if (!Enabled || string.IsNullOrEmpty(PipeName))
                return false;

            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous))
                {
                    client.Connect(TimeoutMs);
                    using (var sw = new StreamWriter(client, Encoding.UTF8))
                    {
                        sw.AutoFlush = true;
                        sw.WriteLine(payload ?? "PING");
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
