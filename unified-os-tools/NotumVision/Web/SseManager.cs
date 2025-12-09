using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NotumHUD.Web
{
    public class SseManager
    {
        private class SseClient
        {
            public readonly StreamWriter Writer;
            private readonly HttpListenerResponse _response;
            public SseClient(StreamWriter writer, HttpListenerResponse response) { Writer = writer; _response = response; }
            public void Close() { try { Writer.Close(); _response.Close(); } catch { } }
        }

        private readonly ConcurrentDictionary<Guid, SseClient> _clients = new ConcurrentDictionary<Guid, SseClient>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task _pingTask;
        
        public SseManager()
        {
            _pingTask = Task.Run(PingClientsLoop, _cts.Token);
        }

        public async Task AddClient(HttpListenerContext context)
        {
            var response = context.Response;
            response.ContentType = "text/event-stream";
            response.ContentEncoding = Encoding.UTF8;
            response.KeepAlive = true;
            response.SendChunked = true;

            var writer = new StreamWriter(response.OutputStream, Encoding.UTF8) { AutoFlush = true };
            var client = new SseClient(writer, response);
            var id = Guid.NewGuid();
            _clients.TryAdd(id, client);

            try
            {
                // The connection will be kept alive here. Disconnects are handled when
                // a broadcast write operation fails.
                await Task.Delay(Timeout.Infinite, _cts.Token);
            }
            catch (TaskCanceledException)
            {
                // This will be caught when the server shuts down.
            }
            finally
            {
                RemoveClient(id);
            }
        }

        public void Broadcast(string jsonData)
        {
            var message = $"data: {jsonData}\n\n";
            foreach (var pair in _clients)
            {
                try
                {
                    pair.Value.Writer.Write(message);
                }
                catch
                {
                    // If write fails, assume client is disconnected and remove it
                    RemoveClient(pair.Key);
                }
            }
        }

        public void Shutdown()
        {
            _cts.Cancel();
            foreach (var id in _clients.Keys)
            {
                RemoveClient(id);
            }
        }

        private void RemoveClient(Guid id)
        {
            if (_clients.TryRemove(id, out var client))
            {
                client.Close();
            }
        }

        private async Task PingClientsLoop()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(15000, _cts.Token);
                    Broadcast(": ping");
                }
                catch (TaskCanceledException) 
                {
                    break; 
                }
            }
        }
    }
}