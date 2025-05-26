// WildcardProcessorAPI.cs
using SwarmUI.WebAPI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Text;
using System.Threading;


namespace Spoomples.Extensions.WildcardImporter
{
    [API.APIClass("API routes related to Wildcard Importer extension")]
    public class WildcardImporterAPI
    {
        private readonly WildcardProcessor _processor;
        
        public WildcardImporterAPI(WildcardProcessor processor)
        {
            _processor = processor;
        }

        public void Register()
        {
            API.RegisterAPICall(ProcessWildcards, true, WildcardImporterExtension.WildcardImporterCalls);
            API.RegisterAPICall(GetProcessingStatus, false, WildcardImporterExtension.WildcardImporterCalls);
            API.RegisterAPICall(UndoProcessing, true, WildcardImporterExtension.WildcardImporterCalls);
            API.RegisterAPICall(GetProcessingHistory, false, WildcardImporterExtension.WildcardImporterCalls);
            API.RegisterAPICall(ResolveConflict, true, WildcardImporterExtension.WildcardImporterCalls);
            API.RegisterAPICall(GetDestinationFolder, false, WildcardImporterExtension.WildcardImporterCalls);
            // API.RegisterAPICall(ProcessWildcardsWithStatus, true, WildcardImporterExtension.WildcardImporterCalls);
            // API.RegisterAPICall(SetDestinationFolder, true, WildcardImporterExtension.WildcardImporterCalls);
        }

        [API.APIDescription("Process wildcard files", "{ success: boolean, message: string, taskId: string }")]
        public async Task<JObject> ProcessWildcards([API.APIParameter("Files to process")] string filesJson, [API.APIParameter("Name to save the files under")]string name)
        {
            try
            {
                var filesData = JsonConvert.DeserializeObject<List<FileData>>(filesJson);
                // var files = filesData.ToObject<List<FileData>>();
                string taskId = await _processor.ProcessFiles(filesData, name);
                return new JObject
                {
                    ["success"] = true,
                    ["message"] = "Processing started",
                    ["taskId"] = taskId
                };
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["message"] = $"Error starting processing: {ex.Message}"
                };
            }
        }

        [API.APIDescription("Get the status of wildcard processing", "{ status: string, conflicts: array, infiles: number, outfiles: number, infilesProcessed: number, outfilesProcessed: number }")]
        public async Task<JObject> GetProcessingStatus(Session session, string taskId)
        {
            var status = _processor.GetStatus(taskId);
            return JObject.FromObject(status);
        }

        [API.APIDescription("Undo the last processing operation", "{ success: boolean, message: string }")]
        public async Task<JObject> UndoProcessing(Session session, string taskId)
        {
            bool success = await _processor.UndoProcessing(taskId);
            return new JObject
            {
                ["success"] = success,
                ["message"] = success ? "Processing undone successfully" : "Failed to undo processing"
            };
        }

        [API.APIDescription("Get the history of processing operations", "{ history: array }")]
        public async Task<JObject> GetProcessingHistory(Session session)
        {
            var history = _processor.GetHistory();
            return new JObject
            {
                ["history"] = JArray.FromObject(history)
            };
        }

        [API.APIDescription("Resolve a file conflict", "{ success: boolean, message: string }")]
        public async Task<JObject> ResolveConflict(Session session, string taskId, string filePath, string resolution)
        {
            bool success = await _processor.ResolveConflict(taskId, filePath, resolution);
            return new JObject
            {
                ["success"] = success,
                ["message"] = success ? "Conflict resolved" : "Failed to resolve conflict"
            };
        }

        [API.APIDescription("Get the current destination folder", "{ folderPath: string }")]
        public async Task<JObject> GetDestinationFolder(Session session)
        {
            string folderPath = _processor.destinationFolder;
            return new JObject
            {
                ["folderPath"] = folderPath ?? ""
            };
        }

        // TODO: Implement this
        // [API.APIDescription("Set the destination folder", "{ success: boolean, message: string }")]
        // public async Task<JObject> SetDestinationFolder(Session session, string folderPath)
        // {
        //     bool success = _processor.SetDestinationFolder(folderPath);
        //     return new JObject
        //     {
        //         ["success"] = success,
        //         ["message"] = success ? "Destination folder set successfully" : "Failed to set destination folder"
        //     };
        // }

        // TODO: In progress
        // public async Task ProcessWildcardsWithStatus(WebSocket socket, Session session,
        // [API.APIParameter("The number of images to generate.")] int images,
        // [API.APIParameter("Raw mapping of input should contain general T2I parameters (see listing on Generate tab of main interface) to values, eg `{ \"prompt\": \"a photo of a cat\", \"model\": \"OfficialStableDiffusion/sd_xl_base_1.0\", \"steps\": 20, ... }`. Note that this is the root raw map, ie all params go on the same level as `images`, `session_id`, etc.")] JObject rawInput)
        // {
        //     if (context.WebSockets.IsWebSocketRequest)
        //     {
        //         using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
        //         string filesJson = context.Request.Query["filesJson"];

        //         if (string.IsNullOrEmpty(filesJson))
        //         {
        //             await SendMessageAsync(webSocket, "Error: 'filesJson' parameter is missing.");
        //             await webSocket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "Missing filesJson", CancellationToken.None);
        //             return;
        //         }

        //         try
        //         {
        //             var filesData = JsonConvert.DeserializeObject<List<FileData>>(filesJson);
        //             string taskId = await _processor.ProcessFilesAsync(filesData, async (status) =>
        //             {
        //                 string statusJson = JsonConvert.SerializeObject(status);
        //                 await SendMessageAsync(webSocket, statusJson);
        //             });

        //             await SendMessageAsync(webSocket, JsonConvert.SerializeObject(new
        //             {
        //                 success = true,
        //                 message = "Processing started",
        //                 taskId = taskId
        //             }));

        //             // Wait for the processing to complete
        //             await _processor.WaitForProcessingToCompleteAsync(taskId);
        //             await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Processing completed", CancellationToken.None);
        //         }
        //         catch (Exception ex)
        //         {
        //             await SendMessageAsync(webSocket, $"Error starting processing: {ex.Message}");
        //             await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Processing error", CancellationToken.None);
        //         }
        //     }
        //     else
        //     {
        //         context.Response.StatusCode = 400;
        //         await context.Response.WriteAsync("WebSocket connection expected.");
        //     }
        // }

        // private async Task SendMessageAsync(WebSocket socket, string message)
        // {
        //     var messageBuffer = Encoding.UTF8.GetBytes(message);
        //     var segment = new ArraySegment<byte>(messageBuffer);
        //     await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
        // }
    }
}
