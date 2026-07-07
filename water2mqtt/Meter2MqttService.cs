using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Exceptions;

namespace water2mqtt;

public class Meter2MqttService : BackgroundService
{
    private readonly IConfiguration config;
    private readonly ILogger<Program> log;
    private readonly IWaterMeter meter;

    public Meter2MqttService(IConfiguration config, ILogger<Program> log, IWaterMeter meter)
    {
        this.config = config;
        this.log = log;
        this.meter = meter;

        topicRoot = $"water/{meter.Manufacturer}{meter.Model}";
    }

    async Task<IMqttClient> Reconnect(CancellationToken cancellationToken)
    {
        var login = config["mqtt:username"];
        var password = config["mqtt:password"];
        var server = config["mqtt:broker"];

        var mqttOptions = new MqttClientOptionsBuilder()
            .WithClientId("water2mqtt")
            .WithTcpServer(server)
            .WithCredentials(login, password)
            .Build();

        var mqttFactory = new MqttFactory();
        var mqttClient = mqttFactory.CreateMqttClient();

        while (true)
        {
            try
            {
                log.LogInformation("Connecting to MQTT broker");
                var connectResult = await mqttClient.ConnectAsync(mqttOptions, cancellationToken);

                if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                {
                    log.LogError($"Connect error: {connectResult.ResultCode}");
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                    continue;
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex.Message);
                log.LogError("Connection failed. Retrying in 5 sec.");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                continue;
            }

            return mqttClient;
        }
    }

    private IMqttClient? client;
    private readonly string topicRoot;

    async Task Publish(Volume value, FlowRate? flowRate, CancellationToken cancel)
    {
        while (true)
        {
            try
            {
                var cubes = value.ToCubicMeters();

                await client.PublishStringAsync(topicRoot + "/WaterConsumption",
                    string.Create(CultureInfo.InvariantCulture, $"{cubes}"), cancellationToken: cancel);

                if (flowRate != null)
                {
                    await client.PublishStringAsync(topicRoot + "/WaterFlowRate",
                        string.Create(CultureInfo.InvariantCulture, $"{flowRate.ToLitersPerMinute()}"),
                        cancellationToken: cancel);
                }

                await client.PublishStringAsync(topicRoot + "/WaterFaucet", flowRate != null && flowRate > FlowRate.Zero ? "ON" : "OFF", cancellationToken: cancel);

                log.LogInformation($"Published new value: {value}");
                break;
            }
            catch (MqttClientNotConnectedException ex)
            {
                log.LogError(ex, "Publish failed");
                client = await Reconnect(cancel);
            }
        }
    }

    // state class: total_increasing   SensorStateClass.TOTAL_INCREASING
    // device_class: energy
    // unit_of_measurement: kWh

    //A correct unit.
    // A correct state_class.
    // A correct device_class.

    async Task HomeAssistantAutoConfig(IMqttClient client, string mqttTopicRoot, CancellationToken cancel)
    {
        var deviceSerialNumber = meter.SerialNumber;
        var uniqueId = $"{meter.Manufacturer}{meter.Model}" + deviceSerialNumber;
        var device = new
        {
            manufacturer = meter.Manufacturer,
            name = "Water meter",
            model = meter.Model,
            identifiers = new[]
            {
                uniqueId
            }
        };

        var waterAutoConfig = new
        {
            platform = "sensor",
            state_topic = mqttTopicRoot + "/WaterConsumption",
            state_class = "total_increasing",
            device_class = "water",
            value_template = "{{ value }}",
            unit_of_measurement = "m\u00b3",
            name = "Water Consumption",
            unique_id = uniqueId + "WaterConsumption",
        };

        var flowRateAutoConfig = new
        {
            platform = "sensor",
            state_topic = mqttTopicRoot + "/WaterFlowRate",
//             state_class = "total_increasing",
            device_class = "volume_flow_rate",
            value_template = "{{ value }}",
            unit_of_measurement = "L/min",
            name = "Water flow rate",
            unique_id = uniqueId + "WaterFlowrate",
        };

        var waterOnOffAutoConfig = new
        {
            platform = "binary_sensor",
            state_topic = mqttTopicRoot + "/WaterFaucet",
            //             state_class = "total_increasing",
            //device_class = "volume_flow_rate",
            value_template = "{{ value }}",
            //unit_of_measurement = "L/min",
            name = "Water faucet",
            unique_id = uniqueId + "WaterFaucet",
        };

        var configTopic = $"homeassistant/device/{uniqueId}/config";

        var deviceDiscoveryPayload = new
        {
            device,
            origin = new
            {
                name = "water2mqtt",
                sw_version = "0.2",
                url = "https://example.com"
            },
            components = new
            {
                ZennerMNKWaterConsumption = waterAutoConfig,
                ZennerMNKWaterFlowRate = flowRateAutoConfig,
                ZennerMNKWaterFaucet = waterOnOffAutoConfig
            }
        };

        var serialized = JsonSerializer.Serialize(deviceDiscoveryPayload);

        await client.PublishAsync(new MqttApplicationMessage
        {
            Topic = configTopic,
            PayloadSegment = Encoding.UTF8.GetBytes(serialized),
            Retain = true
        }, cancel);
    }

    protected override async Task ExecuteAsync(CancellationToken cancel)
    {
        client = await Reconnect(cancel);

        await HomeAssistantAutoConfig(client, topicRoot, cancel);

        //await meter.Start();

        log.LogInformation("Meter started");

        try
        {
            while (!cancel.IsCancellationRequested)
            {
                var value = await meter.GetNextValue(cancel);
                await Publish(value.Volume, value.FlowRate, cancel);
            }
        }
        catch (OperationCanceledException)
        {
        }

        log.LogInformation("Stopping meter");
        //await meter.Stop();
    }
}