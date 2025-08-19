namespace HeroMessaging.Abstractions.Serialization;

public interface IMessageSerializer
{
    byte[] Serialize<T>(T message);
    
    T? Deserialize<T>(byte[] data);
    
    string SerializeToString<T>(T message);
    
    T? DeserializeFromString<T>(string data);
}