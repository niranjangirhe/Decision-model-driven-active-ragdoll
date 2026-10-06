using System;
using System.Net.Http;
using System.Text;
using UnityEngine;

namespace ActiveRagdoll
{
    /// <summary>
    /// Selectors C (Laya) and D (GLiClass): POST the event text to the local decision server.
    ///
    /// Uses System.Net.Http on the thread pool rather than UnityWebRequest, so the answer arrives
    /// even while a scripted batch run keeps the main thread busy. The callback runs on a
    /// background thread; <see cref="ReactionManager"/> only stores the result there.
    ///
    /// GLiClass only chooses the behaviour; its stiffness and recover flag come from the rules
    /// selector for the same event, as the experiment design asks.
    /// </summary>
    public class HttpModelSelector : IBehaviourSelector
    {
        public static string ServerUrl = "http://127.0.0.1:8765";
        static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        readonly string model;
        readonly RulesSelector rules = new RulesSelector();

        static readonly System.Collections.Generic.HashSet<string> warmed = new System.Collections.Generic.HashSet<string>();

        public HttpModelSelector(string model)
        {
            this.model = model;
            // The first request of a session pays for connection setup and JIT: a GET warm-up was not
            // enough, the first real POST still took 237 ms and timed out. So send two throwaway
            // /decide requests through the same code path, once per model, before any trial starts.
            if (warmed.Add(model))
            {
                for (int i = 0; i < 2; i++)
                {
                    try
                    {
                        string body = JsonUtility.ToJson(new RequestBody { text = "Character is standing.", allowed = Behaviours.All, model = model });
                        var resp = client.PostAsync(ServerUrl + "/decide", new StringContent(body, Encoding.UTF8, "application/json")).Result;
                        JsonUtility.FromJson<ResponseBody>(resp.Content.ReadAsStringAsync().Result);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[HttpModelSelector] decision server not reachable: " + (e.InnerException ?? e).Message);
                        break;
                    }
                }
            }
        }
        public string Name => model;

        [Serializable] class RequestBody { public string text; public string[] allowed; public string model; }

        [Serializable]
        class ResponseBody
        {
            public string behaviour;
            public string[] behaviours;
            public float[] probabilities;
            // GLiClass sends null for these; JsonUtility reads that as 0, so the raw JSON is checked below.
            public float stiffness;
            public bool recover;
            public float recover_probability;
            public float latency_ms;
            public string model;
            public string device;
        }

        public void Request(CharacterEvent ev, string eventText, string[] allowed, Action<Decision> onDone)
        {
            string body = JsonUtility.ToJson(new RequestBody { text = eventText, allowed = allowed, model = model });
            // Rules answer for the parameters GLiClass does not provide; computed now, on the main thread.
            Decision rulesDecision = rules.Decide(ev);

            client.PostAsync(ServerUrl + "/decide", new StringContent(body, Encoding.UTF8, "application/json"))
                .ContinueWith(t =>
                {
                    var d = new Decision { Selector = model };
                    try
                    {
                        var resp = t.Result;
                        string json = resp.Content.ReadAsStringAsync().Result;
                        if (!resp.IsSuccessStatusCode) throw new Exception($"HTTP {(int)resp.StatusCode}: {json}");
                        var r = JsonUtility.FromJson<ResponseBody>(json);
                        d.Behaviour = r.behaviour;
                        for (int i = 0; i < r.behaviours.Length; i++) d.Probabilities[r.behaviours[i]] = r.probabilities[i];
                        bool hasParams = !json.Contains("\"stiffness\":null");
                        d.Stiffness = hasParams ? r.stiffness : rulesDecision.Stiffness;
                        d.Recover = hasParams ? r.recover : rulesDecision.Recover;
                        d.ServerLatencyMs = r.latency_ms;
                        d.Device = r.device;
                    }
                    catch (Exception e)
                    {
                        d.Behaviour = null;
                        d.Error = (e.InnerException ?? e).Message;
                    }
                    onDone(d);
                });
        }
    }
}
