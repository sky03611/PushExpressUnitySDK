// Unity Push.Express plugin was developed by eSkukza Apps4You 17.06.24
// Original plugin by Hek
// Tested on Unity v2022.3.32f1
// Apps4You Telegram: @apps4you_rent

// Instalation
// Put PushExpressHandler.prefab on scene
// Write your AppId, make changes in platform, put google-services.json or Google-services.plist into Assets folder and thats it!
// Call your methods after push init or error line 50 ProceedToYourMethod()
// All Debug methods can be deleted without any harm!

// NewtonSoft
// This package requires Newtonsoft Json for unity!!!
// PackageManager => Add by name => com.unity.nuget.newtonsoft-json

// Firebase messaging
// This package requires Firebase messaging!!!
// https://firebase.google.com/download/unity you can get it here
// Don't forget to put google-services.json or Google-services.plist into Assets folder!
// Don't forget to resolve libraries for android!
// Using UniWebView could result in dependency errors on an android platform due to FB. Disable Kotlin, and android.x in UniWebView preferences. Don't do anything anything if you don't have error!
// PS. You can always refer to Firebase Messaging documentation for additional info
// PS2. Push notification images work by default, you don't need to do anything!

using AppsFlyerSDK;
using Firebase.Messaging;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

public class PushExpress : MonoBehaviour
{
    public static PushExpress Instance { get; private set; }
    [Header("PushExpress Settings")]
    [SerializeField][Tooltip("Get App Id on https://push.express")] private string _appId = "20523-1212";
    [SerializeField][Tooltip("Instance update inteval, don't change if not needed")] private float _updateInterval = 120f;
    [SerializeField][Tooltip("Set platform. This will be shown in statistics on push.express")] private Platform _platform = Platform.Ios;
    [SerializeField][Tooltip("Set platform name. This will be shown in statistics on push.express")] private string _platformName = "ios_17";
    [Header("Debug")]
    [SerializeField] private bool _debugEnabled;
    [SerializeField] private TMP_Text _debugText;

    private NotificationStatus _notificationStatus;
    private string _idFb;
    private string _icId;
    private string _extId;

    private void ProceedToYourMethod()
    {
        //...code after push launched (or got error)
    }

    private void SetDebugMessage(string data)
    {
        if(_debugEnabled == false) { return; }
        if(_debugText != null)
            _debugText.text += "\n" + data; 
    }

    private void Awake()
    {
        if (!PlayerPrefs.HasKey("extid"))
        {
            _extId = GenerateExtId();
            PlayerPrefs.SetString("extid", _extId);
        }
            
        else
        {
            _extId = PlayerPrefs.GetString("extid");
        }
        
        if (_debugEnabled)
        {
            Debug.LogWarning("Debugging is enabled! Make sure to disable it before release"); //Doesn't do anything actually but sends text
#if UNITY_EDITOR
            Debug.LogWarning("Push obviously won't work in editor, fb token also won't be received! PushExpress stats, though will be displayed"); //Doesn't do anything actually but sends text
#endif
        }
        //Not really nescessary if you do everything on one scene
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
        //
        _icId = PlayerPrefs.GetString("px_ic_id", null);
        SetDebugMessage("APP ID:" + _appId);
        SetDebugMessage("Received saved ICID:" +_icId);
        InitializeFB();
    }

    private void Start()
    {
        // In 99.99% Firebase gets FBToken (OnTokenReceived() - prefs fbid) before initializing PushExpress
        // If it doesn't, consider Init push express AFTER token is received ann FB Initialized! EX: Just before loading your method ProceedToYourMethod()
        // Note if you want to popup disclaimer about notification ONLY IF something loaded, call InitializeFB() then -> StartPushExpress() in your methods via Instance and delete these methods from Start and Awake
        // For default behaivour use start and awake methods.
        StartPushExpress();
    }

    public void InitializeFB()
    {
        SetDebugMessage("Starting FB Initialization");
        try
        {
            Firebase.FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
            {
                var dependencyStatus = task.Result;
                if (dependencyStatus == Firebase.DependencyStatus.Available)
                {
                    Firebase.FirebaseApp app = Firebase.FirebaseApp.DefaultInstance;
                }
                else
                {
                    Debug.LogError(String.Format("Couldn't resolve all Firebase dependencies: {0}", dependencyStatus));
                }
                FirebaseMessaging.TokenReceived += OnTokenReceived;
                FirebaseMessaging.MessageReceived += OnMessageReceived;
                SetDebugMessage("FB: Firebase Initialized");
                ProceedToYourMethod();
            });

        }
        catch (Exception ex)
        {
            SetDebugMessage("FB: Firebase error: "+ ex.Message);
            ProceedToYourMethod();
        }
    }

    private void OnTokenReceived(object sender, TokenReceivedEventArgs token)
    {
        PlayerPrefs.SetString("idfb", token.Token);
        SetDebugMessage("FB: FBID Token Received: " + token.Token);
    }

    private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
    {
        HandleNotificationDelivered(e.Message.MessageId);
        if (e.Message.Data.TryGetValue("click_action", out string clickAction))
        {
            if (clickAction == "OPEN_ACTIVITY")
            {
                HandleNotificationClick(e.Message.MessageId);
            }
        }
    }

    private void HandleNotificationClick(string data)
    {
        SetDebugMessage("FB: Handle Notification click: " + data);
        SendNotificationEvent(data, NotificationStatus.Clicked);
    }

    private void HandleNotificationDelivered(string data)
    {
        SetDebugMessage("FB: Handle Notification delivered: " + data);
        SendNotificationEvent(data, NotificationStatus.Delivered);
    }

    public void StartPushExpress()
    {
        if (string.IsNullOrEmpty(_appId))
        {
            SetDebugMessage("PE: AppID is not set");
            Debug.LogWarning("PE: App ID is not set");
            return;
        }

        if (string.IsNullOrEmpty(_icId))
        {
            SetDebugMessage("PE: IDID is empty -> getting ICID from PushExpress");
            Debug.Log("IDID is empty -> getting ICID from PushExpress");
            StartCoroutine(CreateAppInstanceCoroutine());
        }
        else
        {
            StartCoroutine(UpdateAppInstanceCoroutine());
            InvokeRepeating(nameof(UpdateAppInstancePeriodically), _updateInterval, _updateInterval);
        }
    }

    private IEnumerator CreateAppInstanceCoroutine()
    {
        var task = CreateAppInstance();
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.Exception != null)
        {
            StartCoroutine(RetryCreateAppInstance());
        }
    }

    private async Task CreateAppInstance()
    {
        var url = $"https://core.push.express/api/r/v2/apps/{_appId}/instances";
        var id = Guid.NewGuid().ToString().ToLower();
        Debug.Log(_extId);
        var paramsDict = new Dictionary<string, string> { { "ic_token", id },
                                                          { "ext_id", _extId } };
        var jsonParams = JsonConvert.SerializeObject(paramsDict);

        using (var client = new HttpClient())
        {
            var content = new StringContent(jsonParams, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("App instance failed to create");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseContent);
            Debug.Log(responseContent);
            if (jsonResponse != null && jsonResponse.TryGetValue("id", out var idObj))
            {
                _icId = idObj.ToString();
                PlayerPrefs.SetString("px_ic_id", _icId);
                SetDebugMessage("PE: ICID received: ICID :" + _icId);
                Debug.Log("ICID received: ICID: " + _icId);
                StartCoroutine(UpdateAppInstanceCoroutine());
                InvokeRepeating(nameof(UpdateAppInstancePeriodically), _updateInterval, _updateInterval);
            }
            else
            {
                SetDebugMessage("PE: Failed to parse response for app instance creation");

                throw new Exception("Failed to parse response for app instance creation");
            }
        }
    }

    private IEnumerator RetryCreateAppInstance()
    {
        var initialDelay = UnityEngine.Random.Range(1f, 5f);
        var maxDelay = 120f;
        var delay = initialDelay;

        while (true)
        {
            yield return new WaitForSeconds(delay);
            var task = CreateAppInstance();
            yield return new WaitUntil(() => task.IsCompleted);

            if (task.Exception == null)
            {
                yield break;
            }

            delay = Mathf.Min(delay * 2, maxDelay);
        }
    }

    private int GetTimeZoneOffsetInSeconds()
    {
        return (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalSeconds;
    }

    private IEnumerator UpdateAppInstanceCoroutine()
    {
        var task = UpdateAppInstance();
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.Exception != null)
        {
            StartCoroutine(RetryUpdateAppInstance());
        }
    }

    private string HandleNotificationStatus(NotificationStatus status)
    {
        switch (status)
        {
            case NotificationStatus.Clicked:
                return "clicked";
            case NotificationStatus.Delivered:
                return "delivered";
            default:
                return "Unknown";
        }
    }

    private async Task UpdateAppInstance()
    {
        var url = $"https://core.push.express/api/r/v2/apps/{_appId}/instances/{_icId}/info";
        string platform = String.Empty;
        if (_platform == Platform.Android)
        {
            platform = "android";
        }
        else
        {
            platform = "ios";
        }
        var paramsDict = new Dictionary<string, object>
        {
            { "transport_type", "fcm" },
            { "transport_token", PlayerPrefs.GetString("idfb", "") },
            { "platform_type", platform },
            { "platform_name", _platformName },
            { "ext_id", "" },
            { "lang", "" },
            { "country", "" },
            { "tz_sec", GetTimeZoneOffsetInSeconds() }
        };
        var jsonParams = JsonConvert.SerializeObject(paramsDict);
        SetDebugMessage("PE: Params Final:" + "transport_token: " + PlayerPrefs.GetString("idfb", ""));
        SetDebugMessage("PE: Params Final:" + "platform_type: " + platform);
        SetDebugMessage("PE: Params Final:" + "platform_name: " + _platformName);
        SetDebugMessage("PE: Params Final:" + "tz_sec: " + GetTimeZoneOffsetInSeconds());
        using (var client = new HttpClient())
        {
            var content = new StringContent(jsonParams, Encoding.UTF8, "application/json");
            var response = await client.PutAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("Failed to update app instance");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseContent);

            if (jsonResponse != null && jsonResponse.TryGetValue("update_interval_sec", out var intervalObj))
            {
                _updateInterval = Convert.ToSingle(intervalObj);
                Debug.Log("UPD Interval " + _updateInterval);
            }
            else
            {
                throw new Exception("Failed to parse response for app instance update");
            }
        }
    }


    private IEnumerator RetryUpdateAppInstance()
    {
        var initialDelay = UnityEngine.Random.Range(1f, 5f);
        var maxDelay = 120f;
        var delay = initialDelay;

        while (true)
        {
            yield return new WaitForSeconds(delay);
            var task = UpdateAppInstance();
            yield return new WaitUntil(() => task.IsCompleted);

            if (task.Exception == null)
            {
                yield break;
            }

            delay = Mathf.Min(delay * 2, maxDelay);
        }
    }

    private void UpdateAppInstancePeriodically()
    {
        StartCoroutine(UpdateAppInstanceCoroutine());
    }

    public void SendNotificationEvent(string msgId, NotificationStatus eventMsg)
    {
        var notificationStatus = HandleNotificationStatus(eventMsg);
        var paramsDict = new Dictionary<string, string>
        {
            { "msg_id", msgId },
            { "event", notificationStatus }
        };
        StartCoroutine(SendEventCoroutine("notification", paramsDict));
    }

    public void SendLifecycleEvent(string eventMsg)
    {
        var paramsDict = new Dictionary<string, string>
        {
            { "event", eventMsg }
        };
        StartCoroutine(SendEventCoroutine("lifecycle", paramsDict));
    }

    private IEnumerator SendEventCoroutine(string endpoint, Dictionary<string, string> paramsDict)
    {
        var task = SendEvent(endpoint, paramsDict);
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.Exception != null)
        {
            Debug.LogError($"Failed to send {endpoint} event: {task.Exception}");
        }
    }

    private async Task SendEvent(string endpoint, Dictionary<string, string> paramsDict)
    {
        var url = $"https://core.push.express/api/r/v2/apps/{_appId}/instances/{_icId}/events/{endpoint}";
        var jsonParams = JsonConvert.SerializeObject(paramsDict);

        using (var client = new HttpClient())
        {
            var content = new StringContent(jsonParams, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Failed to send {endpoint} event");
            }
        }
    }

    public string GenerateExtId()
    {
        int randomNumber = UnityEngine.Random.Range(1, 1000001);
        string formattedNumber = randomNumber.ToString("D7");
        return formattedNumber;
    }
}
public enum NotificationStatus
{
    Clicked,
    Delivered
}
public enum Platform
{
    Android,
    Ios
}
