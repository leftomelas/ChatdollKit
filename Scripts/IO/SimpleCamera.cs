using System;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif
using Cysharp.Threading.Tasks;

namespace ChatdollKit.IO
{
    public class SimpleCamera : MonoBehaviour
    {
        [SerializeField]
        private RawImage previewWindow;
        [SerializeField]
        private string deviceName;
        [SerializeField]
        private Vector2Int size = new Vector2Int(640, 480);
        [SerializeField]
        private int fps = 10;
        [SerializeField]
        private float launchTimeout = 10.0f;
        [SerializeField]
        private float waitAfterStart = 0.5f;
        [SerializeField]
        private string savePathForDebug;

        public bool IsCameraEnabled { get; private set; } = false;
        public bool IsAlreadyStarted { get; private set; } = false;
        private WebCamTexture webCamTexture;

        private void Awake()
        {
#if PLATFORM_ANDROID
            // Request permission if Android
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Permission.RequestUserPermission(Permission.Camera);
            }
#endif
        }

        private void Update()
        {
            if (!IsCameraEnabled)
            {
#if PLATFORM_ANDROID
                // Check permission if Android
                if (Permission.HasUserAuthorizedPermission(Permission.Camera))
                {
                    IsCameraEnabled = true;
                }
                else
                {
                    return;
                }
#else
                IsCameraEnabled = true;
#endif
            }
        }

        private void OnDestroy()
        {
            Stop();
        }

        public async UniTask StartAsync(bool showPreview = true)
        {
            if (IsAlreadyStarted) return;

            try
            {
                // Configure and start camera
                webCamTexture = new WebCamTexture(deviceName, size.x, size.y, fps > 0 ? fps : 10);
                previewWindow.texture = webCamTexture;
                webCamTexture.Play();

                // Wait
                if (!await WaitForReadyAsync(launchTimeout))
                {
                    Debug.LogError($"Failed to launch camera in {launchTimeout} seconds");
                    return;
                }

                // Preview
                if (showPreview)
                {
                    AdjustAspectRatio();

                    foreach (var device in WebCamTexture.devices)
                    {
                        if (device.name == webCamTexture.deviceName)
                        {
                            if (device.isFrontFacing)
                            {
                                previewWindow.transform.rotation = Quaternion.Euler(0, 180, -webCamTexture.videoRotationAngle);
                            }
                            else
                            {
                                // should be 0, not 180. but iOS requires 180 :(
                                previewWindow.transform.rotation = Quaternion.Euler(0, 180, -webCamTexture.videoRotationAngle);
                            }
                        }
                    }

                    previewWindow.gameObject.SetActive(true);
                }

                IsAlreadyStarted = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error occured in starting camera: {ex.Message}\n{ex.StackTrace}");
                webCamTexture?.Stop();
            }
        }

        public void Stop()
        {
            previewWindow.gameObject.SetActive(false);
            webCamTexture?.Stop();
            IsAlreadyStarted = false;
        }

        public void ToggleCamera()
        {
            if (IsAlreadyStarted)
            {
                Stop();
            }
            else
            {
                _ = StartAsync();
            }
        }

        public async UniTask<byte[]> CaptureImageAsync()
        {
            // Start camera if not started
            var cameraIsStarted = IsAlreadyStarted;
            if (!cameraIsStarted)
            {
                await StartAsync(showPreview: false);
                await UniTask.Delay((int)(waitAfterStart * 1000));   // Wait a bit to ensure capturing
            }

            // Take photo
            var photo = new Texture2D(webCamTexture.width, webCamTexture.height);
            photo.SetPixels32(webCamTexture.GetPixels32());
            photo.Apply();

            photo = RotateTexture(photo, webCamTexture.videoRotationAngle);

            // Stop camera if started at this method
            if (!cameraIsStarted)
            {
                Stop();
            }

            // Encode to JPG
            var jpg = photo.EncodeToJPG();

            // Save for debug
            if (!string.IsNullOrEmpty(savePathForDebug))
            {
                var st = new FileStream(savePathForDebug, FileMode.OpenOrCreate, FileAccess.Write);
                await st.WriteAsync(jpg, 0, jpg.Length).AsUniTask();
            }

            return jpg;
        }

        public WebCamDevice[] GetDevices()
        {
            return WebCamTexture.devices;
        }

        private async UniTask<bool> WaitForReadyAsync(float timeout)
        {
            var startTime = Time.time;
            while (webCamTexture == null
                || !webCamTexture.isPlaying
                || !webCamTexture.isReadable
                || webCamTexture.width != webCamTexture.requestedWidth
                || webCamTexture.height != webCamTexture.requestedHeight
                || previewWindow.texture != webCamTexture)
            {
                await UniTask.Delay(100);
                if (Time.time - startTime > timeout)
                {
                    return false;
                }
            }

            return true;
        }

        private void AdjustAspectRatio()
        {
            var imageAspectRatio = (float)webCamTexture.width / webCamTexture.height;
            var windowAspectRatio = previewWindow.rectTransform.rect.width / previewWindow.rectTransform.rect.height;

            if (imageAspectRatio > windowAspectRatio)
            {
                var scaleHeight = windowAspectRatio / imageAspectRatio;
                previewWindow.rectTransform.localScale = new Vector3(1, scaleHeight, 1);
            }
            else
            {
                var scaleWidth = imageAspectRatio / windowAspectRatio;
                previewWindow.rectTransform.localScale = new Vector3(scaleWidth, 1, 1);
            }
        }

        private Texture2D RotateTexture(Texture2D originalTexture, int angle)
        {
            int width = originalTexture.width;
            int height = originalTexture.height;
            Texture2D rotatedTexture;

            angle = (angle % 360 + 360) % 360;

            if (angle == 270)
            {
                rotatedTexture = new Texture2D(height, width);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        rotatedTexture.SetPixel(height - y - 1, x, originalTexture.GetPixel(x, y));
                    }
                }

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        rotatedTexture.SetPixel(y, width - x - 1, originalTexture.GetPixel(x, y));
                    }
                }

            }
            else if (angle == 180)
            {
                rotatedTexture = new Texture2D(width, height);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        rotatedTexture.SetPixel(width - x - 1, height - y - 1, originalTexture.GetPixel(x, y));
                    }
                }
            }
            else if (angle == 90)
            {
                rotatedTexture = new Texture2D(height, width);
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        rotatedTexture.SetPixel(y, width - x - 1, originalTexture.GetPixel(x, y));
                    }
                }
            }
            else
            {
                rotatedTexture = new Texture2D(width, height);
                Graphics.CopyTexture(originalTexture, rotatedTexture);
            }

            rotatedTexture.Apply();
            return rotatedTexture;
        }
    }
}
