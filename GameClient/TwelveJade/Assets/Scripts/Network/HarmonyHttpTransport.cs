#if UNITY_OPENHARMONY && !UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;

namespace TwelveJade.Network
{
    // Only the transport changes; authentication, retry, refresh and save rules stay shared.
    public sealed class HarmonyHttpTransport : NetworkClient.ITransport
    {
        public IEnumerator Send(string method, string url, string jsonBody, float timeoutSeconds,
            string authToken, Action<NetworkClient.TransportResult> done)
        {
            OpenHarmonyJSClass bridge = null;
            string id = Guid.NewGuid().ToString("N");
            var result = new NetworkClient.TransportResult();
            try
            {
                bridge = new OpenHarmonyJSClass("JadeHttp");
                bridge.CallStatic("start", JsonUtility.ToJson(new Request {
                    id = id, method = method, url = url, body = jsonBody ?? "", token = authToken ?? "",
                    timeoutMs = Mathf.Clamp(Mathf.CeilToInt(timeoutSeconds * 1000), 1000, 60000)
                }));
            }
            catch (Exception error) { result.NetworkErrorMessage = "Harmony network initialization: " + error.GetType().Name; }
            if (result.NetworkErrorMessage != null)
            {
                bridge?.Dispose(); done(result); yield break;
            }
            try
            {
                float deadline = Time.realtimeSinceStartup + Mathf.Clamp(timeoutSeconds, 1, 60) + 2;
                while (Time.realtimeSinceStartup < deadline)
                {
                    string json = null;
                    try { json = bridge.CallStatic<string>("take", id); }
                    catch (Exception error) { result.NetworkErrorMessage = "Harmony network response: " + error.GetType().Name; }
                    if (result.NetworkErrorMessage != null) break;
                    if (!string.IsNullOrEmpty(json))
                    {
                        try
                        {
                            var response = JsonUtility.FromJson<Response>(json);
                            result.HttpStatus = response.status; result.Body = response.body;
                            result.NetworkErrorMessage = string.IsNullOrEmpty(response.error) ? null : response.error;
                        }
                        catch (Exception) { result.NetworkErrorMessage = "Invalid Harmony network response."; }
                        break;
                    }
                    yield return null;
                }
                if (result.HttpStatus == 0 && result.NetworkErrorMessage == null) result.NetworkErrorMessage = "Request timed out.";
            }
            finally
            {
                try { bridge.CallStatic("cancel", id); } catch (Exception) { }
                bridge.Dispose();
            }
            done(result);
        }

        [Serializable] sealed class Request
        {
            public string id, method, url, body, token;
            public int timeoutMs;
        }
        [Serializable] sealed class Response
        {
            public int status;
            public string body, error;
        }
    }
}
#endif
