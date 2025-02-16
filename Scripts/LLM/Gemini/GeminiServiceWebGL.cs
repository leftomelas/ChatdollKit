﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using Newtonsoft.Json;
using Cysharp.Threading.Tasks;
using System.Linq;

namespace ChatdollKit.LLM.Gemini
{
    public class GeminiServiceWebGL : GeminiService
    {
        public override bool IsEnabled
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return _IsEnabled;
#else
                return false;
#endif
            }
        }

#if UNITY_WEBGL
        [DllImport("__Internal")]
        protected static extern void StartGeminiMessageStreamJS(string targetObjectName, string sessionId, string url, string apiKey, string chatCompletionRequest);
        [DllImport("__Internal")]
        protected static extern void AbortGeminiMessageStreamJS();

        protected bool isChatCompletionJSDone { get; set; } = false;
        protected Dictionary<string, GeminiSession> sessions { get; set; } = new Dictionary<string, GeminiSession>();

        public override async UniTask StartStreamingAsync(GeminiSession geminiSession, Dictionary<string, string> customParameters, Dictionary<string, string> customHeaders, bool useFunctions = true, CancellationToken token = default)
        {
            geminiSession.CurrentStreamBuffer = string.Empty;

            // Store session with id to receive streaming data from JavaScript
            var sessionId = Guid.NewGuid().ToString();
            sessions.Add(sessionId, geminiSession);

            // GenerationConfig
            var generationConfig = new GeminiGenerationConfig()
            {
                temperature = Temperature,
                topP = TopP,
                topK = TopK,
                maxOutputTokens = MaxOutputTokens,
                stopSequences = StopSequences
            };

            // Make request data
            var data = new Dictionary<string, object>()
            {
                { "contents", geminiSession.Contexts },
                { "generationConfig", generationConfig }
            };
            foreach (var p in customParameters)
            {
                data[p.Key] = p.Value;
            }

            // TODO: Support custom headers later...
            if (customHeaders.Count > 0)
            {
                Debug.LogWarning("Custom headers for Gemini on WebGL is not supported for now.");
            }

            // Set tools. Multimodal model doesn't support function calling for now (2023.12.29)
            if (useFunctions && Tools.Count > 0 && !Model.ToLower().Contains("vision"))
            {
                data.Add("tools", new List<Dictionary<string, object>>(){
                     new Dictionary<string, object> {
                         { "function_declarations", Tools }
                     }
                 });
            }

            var serializedData = JsonConvert.SerializeObject(data);

            if (DebugMode)
            {
                Debug.Log($"Request to Gemini: {serializedData}");
            }

            // Start API stream
            isChatCompletionJSDone = false;
            StartGeminiMessageStreamJS(
                gameObject.name,
                sessionId,
                string.IsNullOrEmpty(GenerateContentUrl) ? $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:streamGenerateContent" : GenerateContentUrl,
                ApiKey,
                serializedData
            );

            // Preprocessing response
            var noDataResponseTimeoutsAt = DateTime.Now.AddMilliseconds(noDataResponseTimeoutSec * 1000);
            while (true)
            {
                // Success
                if (!string.IsNullOrEmpty(geminiSession.StreamBuffer) && isChatCompletionJSDone)
                {
                    break;
                }

                // Timeout with no response data
                else if (string.IsNullOrEmpty(geminiSession.StreamBuffer) && DateTime.Now > noDataResponseTimeoutsAt)
                {
                    AbortGeminiMessageStreamJS();
                    geminiSession.ResponseType = ResponseType.Timeout;
                    sessions.Remove(sessionId);
                    break;
                }

                // Other errors
                else if (isChatCompletionJSDone)
                {
                    Debug.LogError($"ChatGPT ends with error");
                    geminiSession.ResponseType = ResponseType.Error;
                    break;
                }

                // Cancel
                else if (token.IsCancellationRequested)
                {
                    Debug.Log("Preprocessing response from ChatGPT canceled.");
                    geminiSession.ResponseType = ResponseType.Error;
                    AbortGeminiMessageStreamJS();
                    break;
                }

                await UniTask.Delay(10);
            }

            // Update histories
            if (geminiSession.ResponseType != ResponseType.Error && geminiSession.ResponseType != ResponseType.Timeout)
            {
                UpdateContext(geminiSession);
            }
            else
            {
                Debug.LogWarning($"Messages are not added to histories for response type is not success: {geminiSession.ResponseType}");
            }

            // Ends with error
            if (geminiSession.ResponseType == ResponseType.Error)
            {
                throw new Exception($"Gemini ends with error");
            }

            // Process tags
            var extractedTags = ExtractTags(geminiSession.CurrentStreamBuffer);
            if (extractedTags.Count > 0 && HandleExtractedTags != null)
            {
                HandleExtractedTags(extractedTags, geminiSession);
            }

            if (CaptureImage != null && extractedTags.ContainsKey("vision") && geminiSession.IsVisionAvailable)
            {
                // Prevent infinit loop
                geminiSession.IsVisionAvailable = false;

                // Get image
                var imageSource = extractedTags["vision"];
                var imageBytes = await CaptureImage(imageSource);

                // Make contexts
                if (imageBytes != null)
                {
                    geminiSession.Contexts.Add(new GeminiMessage("model", geminiSession.StreamBuffer));
                    // Image -> Text to get the better accuracy
                    var userMessageWithVision = new GeminiMessage("user", inlineData: new GeminiInlineData("image/jpeg", imageBytes));
                    userMessageWithVision.parts.Add(new GeminiPart(text: $"This is the image you captured. (source: {imageSource})"));
                    geminiSession.Contexts.Add(userMessageWithVision);
                }
                else
                {
                    geminiSession.Contexts.Add(new GeminiMessage("user", "Please inform the user that an error occurred while capturing the image."));
                }

                // Call recursively with image
                await StartStreamingAsync(geminiSession, customParameters, customHeaders, useFunctions, token);
            }
            else
            {
                geminiSession.IsResponseDone = true;

                sessions.Remove(sessionId);

                if (DebugMode)
                {
                    Debug.Log($"Response from Gemini: {JsonConvert.SerializeObject(geminiSession.StreamBuffer)}");
                }
            }
        }

        public void SetGeminiMessageStreamChunk(string chunkStringWithSessionId)
        {
            var splittedChunk = chunkStringWithSessionId.Split("::");
            var sessionId = splittedChunk[0];
            var chunkString = splittedChunk[1];

            if (string.IsNullOrEmpty(chunkString))
            {
                Debug.Log("Chunk is null or empty. Set true to isChatCompletionJSDone.");
                isChatCompletionJSDone = true;
                return;
            }

            if (DebugMode)
            {
                Debug.Log($"Chunk from Gemini: {chunkString}");
            }

            if (!sessions.ContainsKey(sessionId))
            {
                Debug.LogWarning($"Session not found. Set true to isChatCompletionJSDone.: {sessionId}");
                isChatCompletionJSDone = true;
                return;
            }

            var geminiSession = sessions[sessionId];

            // TODO: Local buffer for a chunk data is not deserializable

            var resp = string.Empty;
            var isDone = false;
            if (chunkString.StartsWith("[") || chunkString.StartsWith(","))
            {
                // Remove "[" or "," to parse as JSON
                chunkString = chunkString.Substring(1);

                // Remove trailing "]" to parse as JSON
                if (chunkString.EndsWith("]"))
                {
                    chunkString = chunkString.Substring(0, chunkString.Length - 1);
                    isDone = true;
                }

                var streamResponse = JsonConvert.DeserializeObject<GeminiStreamResponse>(chunkString);
                if (streamResponse == null) return;

                if (streamResponse.candidates[0].content.parts[0].functionCall != null)
                {
                    if (geminiSession.ResponseType == ResponseType.None)
                    {
                        geminiSession.ResponseType = ResponseType.FunctionCalling;
                        if (string.IsNullOrEmpty(geminiSession.FunctionName))
                        {
                            geminiSession.FunctionName = streamResponse.candidates[0].content.parts[0].functionCall.name;
                        }
                    }
                    resp = JsonConvert.SerializeObject(streamResponse.candidates[0].content.parts[0].functionCall.args);
                }
                else
                {
                    if (geminiSession.ResponseType == ResponseType.None)
                    {
                        geminiSession.ResponseType = ResponseType.Content;
                    }
                    resp = streamResponse.candidates[0].content.parts[0].text;
                }
            }
            else if (chunkString.EndsWith("]"))
            {
                isDone = true;
            }

            geminiSession.CurrentStreamBuffer += resp;
            geminiSession.StreamBuffer += resp;

            if (isDone)
            {
                isChatCompletionJSDone = true;
            }
        }
#endif
    }
}
