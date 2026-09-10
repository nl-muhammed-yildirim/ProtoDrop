namespace wa.infrastructure.Events;

/// <summary>
/// T-006b / TA-5.1 — Service Bus topic settings. Frozen default <c>core</c>
/// (TA-5.1 topic inventory); the value comes from <c>Wa:ServiceBus:Topic</c>
/// (see <c>EventPublishingDependency</c>).
/// </summary>
public sealed class ServiceBusTopicOptions
{
    public ServiceBusTopicOptions()
    {
        TopicName = "core";
    }

    public ServiceBusTopicOptions(string topicName)
    {
        TopicName = topicName;
    }

    /// <summary>Topic to publish to (TA-5.1: <c>core</c>).</summary>
    public string TopicName { get; set; }
}