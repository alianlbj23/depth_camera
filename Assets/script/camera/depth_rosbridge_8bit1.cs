using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Collections;

[RequireComponent(typeof(Camera))]
public class depth_rosbridge_8bit1 : MonoBehaviour
{
    [Header("Shader Setup")]
    public Shader uberReplacementShader;

    [Header("ROS Setup")]
    public ConnectRosBridge connectRos;
    public string DepthTopic = "/camera/depth/compressed";
    public string frame_id = "camera";

    [Header("Depth Settings")]
    public int depthWidth = 720;
    public int depthHeight = 480;
    public float publishInterval = 0.1f; // 10 Hz

    private Camera depthCam;
    private RenderTexture depthRT;
    private Texture2D depthTex;

    public GameObject lineRendererObject;
    private LineRenderer lineRenderer;

    void Start()
    {
        // 设置 LineRenderer 不干扰深度
        if (lineRendererObject != null)
        {
            lineRenderer = lineRendererObject.GetComponent<LineRenderer>();
            lineRenderer.sortingLayerName = "IgnoreDepthRendering";
        }

        // 初始化相机
        depthCam = GetComponent<Camera>();
        depthCam.allowMSAA = false;
        depthCam.allowHDR = false;
        depthCam.cullingMask = -1;

        if (!uberReplacementShader)
            uberReplacementShader = Shader.Find("Hidden/UberReplacement");

        SetupCameraWithReplacementShader(depthCam, uberReplacementShader, 2, Color.white);

        // 设置 RenderTexture 和 Texture2D
        depthRT = new RenderTexture(depthWidth, depthHeight, 24, RenderTextureFormat.RFloat);
        depthRT.Create();
        depthCam.targetTexture = depthRT;

        depthTex = new Texture2D(depthWidth, depthHeight, TextureFormat.RGB24, false);

        // 广播 ROS topic
        AdvertiseTopic();

        // 启动协程
        StartCoroutine(CaptureDepthCoroutine());
    }

    IEnumerator CaptureDepthCoroutine()
    {
        yield return new WaitForSeconds(2.0f); // Delay to ensure the topic is fully advertised

        while (true)
        {
            yield return new WaitForSeconds(publishInterval);
            depthCam.Render();

            AsyncGPUReadback.Request(depthRT, 0, req =>
            {
                if (req.hasError)
                {
                    Debug.LogError("[DepthImagePNG] GPU readback error!");
                    return;
                }

                var data = req.GetData<float>();
                ProcessAndPublishDepth(data);
            });
        }
    }


    private void ProcessAndPublishDepth(NativeArray<float> data)
    {
        // 将深度数据存入 Texture2D
        for (int y = 0; y < depthHeight; y++)
        {
            for (int x = 0; x < depthWidth; x++)
            {
                float depth = data[y * depthWidth + x];
                byte gray = (byte)Mathf.Clamp(depth * 255.0f, 0, 255);
                depthTex.SetPixel(x, y, new Color32(gray, gray, gray, 255));
            }
        }
        depthTex.Apply();

        // 使用 PNG 编码
        byte[] pngBytes = depthTex.EncodeToPNG();

        // 发布到 ROS
        Task.Run(() => PublishDepthImage(DepthTopic, pngBytes, frame_id));
    }

    public void PublishDepthImage(string topic, byte[] imagebytes, string frame_id)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        // string imageString = Convert.ToBase64String(imagebytes);
        string imageString = "[" + string.Join(",", imagebytes) + "]";

        string publishMessage = $@"{{
            ""op"": ""publish"",
            ""topic"": ""{topic}"",
            ""msg"": {{
                ""header"": {{
                    ""stamp"": {{
                        ""secs"": {timestamp / 1000},
                        ""nsecs"": {(timestamp % 1000) * 1000000}
                    }},
                    ""frame_id"": ""{frame_id}""
                }},
                ""format"": ""png"",
                ""data"": {imageString}
            }}
        }}";

        connectRos.ws.Send(publishMessage);
    }

    private void AdvertiseTopic()
    {
        if (connectRos == null || connectRos.ws == null)
        {
            Debug.LogError("[Depth ROS Bridge] connectRos or WebSocket is not initialized!");
            return;
        }

        string advertiseMessage = $@"{{
            ""op"": ""advertise"",
            ""topic"": ""{DepthTopic}"",
            ""type"": ""sensor_msgs/msg/CompressedImage""
        }}";
        connectRos.ws.Send(advertiseMessage);
    }


    static private void SetupCameraWithReplacementShader(Camera cam, Shader shader, int mode, Color clearColor)
    {
        var cb = new CommandBuffer();
        cb.SetGlobalFloat("_OutputMode", mode);

        cam.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, cb);
        cam.AddCommandBuffer(CameraEvent.BeforeFinalPass, cb);

        // cam.SetReplacementShader(shader, "");
        cam.backgroundColor = clearColor;
        cam.clearFlags = CameraClearFlags.SolidColor;
    }

    private void OnDestroy()
    {
        if (depthRT != null) depthRT.Release();
    }
}
