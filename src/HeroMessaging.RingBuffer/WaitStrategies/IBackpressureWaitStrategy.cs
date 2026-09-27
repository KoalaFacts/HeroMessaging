namespace HeroMessaging.RingBuffer.WaitStrategies;

internal interface IBackpressureWaitStrategy
{
    void WaitForCapacity(Func<bool> hasCapacity);
}
