using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System;
using TMPro;

[Serializable]
public class WeatherInfo
{
    public string main;
    public string description;
}

[Serializable]
public class MainData
{
    public float temp;
    public float humidity;
}

[Serializable]
public class WeatherResponse
{
    public WeatherInfo[] weather;
    public MainData main;
    public float visibility;
}

public class WeatherController : MonoBehaviour
{
    [Header("Settings")]
    public string apiKey = "2061ee2dade3cf8df97e910e5ac652c1";
    public string latitude = "44.8066";
    public string longitude = "-0.6310";

    [Header("UI Elements")]
    public TextMeshProUGUI weatherText;

    [Header("Visual Effects")]
    public GameObject rainEffect;
    public GameObject cloudEffect;
    public GameObject sunEffect;

    void Start()
    {
        // همه افکت‌ها رو اول خاموش کن
        SetEffect(rainEffect, false);
        SetEffect(cloudEffect, false);
        SetEffect(sunEffect, false);

        StartCoroutine(GetWeather());
    }

    IEnumerator GetWeather()
    {
        string url = $"https://api.openweathermap.org/data/2.5/weather?lat={latitude}&lon={longitude}&appid={apiKey}&units=metric";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                WeatherResponse weatherData = JsonUtility.FromJson<WeatherResponse>(request.downloadHandler.text);

                string condition = weatherData.weather[0].main;

                if (weatherText != null)
                {
                    weatherText.text = $"City: Bordeaux\n" +
                                       $"Weather: {condition}\n" +
                                       $"Temp: {weatherData.main.temp} °C\n" +
                                       $"Humidity: {weatherData.main.humidity}%";
                }

                UpdateVisualEffects(condition);
            }
            else
            {
                Debug.LogError("[WeatherController] Error: " + request.error);

                if (weatherText != null)
                    weatherText.text = "Weather unavailable";
            }
        }
    }

    void UpdateVisualEffects(string condition)
    {
        SetEffect(rainEffect, condition == "Rain" || condition == "Drizzle" || condition == "Thunderstorm");
        SetEffect(cloudEffect, condition == "Clouds" || condition == "Mist" || condition == "Fog");
        SetEffect(sunEffect, condition == "Clear");
    }

    void SetEffect(GameObject effect, bool active)
    {
        if (effect != null)
            effect.SetActive(active);
    }
}