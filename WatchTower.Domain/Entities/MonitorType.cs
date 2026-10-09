using System.Text.Json.Serialization;

namespace WatchTower.Domain.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MonitorType
{
    Website,
    IpAddress,
    Api
}
