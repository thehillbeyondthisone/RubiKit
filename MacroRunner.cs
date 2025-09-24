// MacroRunner.cs
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RubiKit
{
    /// <summary>
    /// Queues and sends macro lines to the game, with delay & stop support.
    /// </summary>
    public static class MacroRunner
    {
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private static CancellationTokenSource _cts;
        private static int _delayMs = 250;
        private static bool _echo = true;
        private static bool _running;

        public static void Enqueue(IEnumerable<string> lines, int delayMs, bool echo)
        {
            foreach (var l in lines)
                if (!string.IsNullOrWhiteSpace(l))
                    _queue.Enqueue(l.Trim());

            _delayMs = Math.Max(0, delayMs);
            _echo = echo;
            EnsureLoop();
        }

        public static void EnqueueOne(string line, bool echo)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            _queue.Enqueue(line.Trim());
            _echo = echo;
            EnsureLoop();
        }

        public static void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            _running = false;
            while (_queue.TryDequeue(out _)) { /* drop */ }
            Chat.WriteLine("[RubiKit] Macro queue stopped.", ChatColor.Gold);
        }

        private static void EnsureLoop()
        {
            if (_running) return;
            _running = true;
            _cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        if (!_queue.TryDequeue(out var next))
                        {
                            _running = false; // drained
                            return;
                        }

                        if (_echo)
                            Chat.WriteLine($"[RubiKit] >> {next}", ChatColor.Green);

                        bool ok = GameChat.TrySendCommand(next, out string err);
                        if (!ok)
                            Chat.WriteLine($"[RubiKit] Send failed: {err}", ChatColor.Orange);

                        await Task.Delay(_delayMs, _cts.Token);
                    }
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    Chat.WriteLine("[RubiKit] Macro loop error: " + ex.Message, ChatColor.Orange);
                }
                finally
                {
                    _running = false;
                }
            }, _cts.Token);
        }
    }

    internal static class GameChat
    {
        // If your AOSharp build exposes a direct sender (e.g., Chat.Send or CommandManager.Execute),
        // replace this body with that call and keep the signature.
        public static bool TrySendCommand(string text, out string error)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) { error = "Empty"; return false; }

                // Works in many AOSharp builds when the text starts with "/"
                Chat.WriteLine(text);

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
