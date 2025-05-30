using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ChatdollKit.Dialog;
using ChatdollKit.IO;
using ChatdollKit.Model;

namespace ChatdollKit.Demo
{
    public class Main08 : MonoBehaviour
    {
        // ChatdollKit components
        private ModelController modelController;
        private DialogProcessor dialogProcessor;
        private SimpleCamera simpleCamera;

        [SerializeField]
        private AGIARegistry.AnimationCollection animationCollectionKey = AGIARegistry.AnimationCollection.AGIAFree;
        [SerializeField]
        private bool ListRegisteredAnimationsOnStart = false;
        [SerializeField]
        private Text titleText;

        private void Start()
        {
            if (titleText != null)
            {
                titleText.text = $"ChatdollKit Demo v{AIAvatar.VERSION}";
            }

            // Get ChatdollKit components
            modelController = gameObject.GetComponent<ModelController>();
            dialogProcessor = gameObject.GetComponent<DialogProcessor>();

            // Image capture for vision
            if (simpleCamera == null)
            {
                simpleCamera = FindFirstObjectByType<SimpleCamera>();
                if (simpleCamera == null)
                {
                    Debug.LogWarning("SimpleCamera is not found in this scene.");
                }
                else
                {
                    dialogProcessor.LLMServiceExtensions.CaptureImage = async (source) =>
                    {
                        try
                        {
                            return await simpleCamera.CaptureImageAsync();
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"Error at CaptureImage: {ex.Message}\n{ex.StackTrace}");
                        }
                        return null;
                    };
                }
            }

            // Register animations
            modelController.RegisterAnimations(AGIARegistry.GetAnimations(animationCollectionKey));
            if (ListRegisteredAnimationsOnStart)
            {
                var animationsList = modelController.ListRegisteredAnimations();
                Debug.Log($"=== Registered Animations ===\n{animationsList}");
            }

            // Animation and face expression for idling
            modelController.AddIdleAnimation("generic", 10.0f);
            modelController.AddIdleAnimation("calm_hands_on_back", 5.0f);

            // // Animation and face expression for processing (Use when the response takes a long time)
            // var processingAnimation = new List<Model.Animation>();
            // processingAnimation.Add(modelController.GetRegisteredAnimation("concern_right_hand_front", 0.3f));
            // processingAnimation.Add(modelController.GetRegisteredAnimation("concern_right_hand_front", 20.0f, "AGIA_Layer_nodding_once_01", "Additive Layer"));
            // var processingFace = new List<FaceExpression>();
            // processingFace.Add(new FaceExpression("Blink", 3.0f));
            // gameObject.GetComponent<AIAvatar>().AddProcessingPresentaion(processingAnimation, processingFace);

            // Animation and face expression for start up
            var animationOnStart = new List<Model.Animation>();
            animationOnStart.Add(modelController.GetRegisteredAnimation("generic", 0.5f));
            animationOnStart.Add(modelController.GetRegisteredAnimation("waving_arm", 3.0f));

            modelController.Animate(animationOnStart);

            var faceOnStart = new List<FaceExpression>();
            faceOnStart.Add(new FaceExpression("Joy", 3.0f));
            modelController.SetFace(faceOnStart);

            // // Long-Term Memory Manager (e.g. ChatMemory https://github.com/uezo/chatmemory)
            // // 1. Add ChatMemoryIntegrator to this game object
            // // 2. Configure ChatMemory url and user id
            // // 3. To retrieve memory in the conversation add ChatMemoryTool to this game object
            // var chatMemory = gameObject.GetComponent<ChatMemoryIntegrator>();
            // dialogProcessor.LLMServiceExtensions.OnStreamingEnd += async (text, payloads, llmSession, token) =>
            // {
            //     // Add history to ChatMemory service
            //     chatMemory.AddHistory(llmSession.ContextId, text, llmSession.CurrentStreamBuffer, token).Forget();
            // };
        }

        private void Update()
        {
            // Advanced usage:
            // Uncomment the following lines to start a conversation in idle mode, with any word longer than 3 characters instead of the wake word.

            // if (aiAvatar.Mode == AIAvatar.AvatarMode.Idle)
            // {
            //     aiAvatar.WakeLength = 3;
            // }
            // else if (aiAvatar.Mode == AIAvatar.AvatarMode.Sleep)
            // {
            //     aiAvatar.WakeLength = 0;
            // }

            // // Uncomment to use AzureStreamSpeechListener
            // if (aiAvatar.Mode == AIAvatar.AvatarMode.Conversation)
            // {
            //     if (!string.IsNullOrEmpty(azureStreamSpeechListener.RecognizedTextBuffer))
            //     {
            //         aiAvatar.UserMessageWindow.Show(azureStreamSpeechListener.RecognizedTextBuffer);
            //     }
            // }
        }
    }
}
