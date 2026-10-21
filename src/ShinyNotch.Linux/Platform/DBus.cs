using Tmds.DBus.Protocol;

namespace ShinyNotch.Linux;

/// <summary>
/// The little D-Bus plumbing the head needs. Tmds.DBus.Protocol is codegen-free, so calls are written and
/// read by hand.
/// </summary>
static class DBus
{
    const string Properties = "org.freedesktop.DBus.Properties";

    public static MessageBuffer Call(DBusConnection connection, string destination, string path, string @interface, string member, string? signature = null, Action<MessageWriter>? write = null)
    {
        var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: destination, path: path, @interface: @interface, member: member, signature: signature);
        write?.Invoke(writer);
        return writer.CreateMessage();
    }

    /// <summary>Every property of an interface, with variants unwrapped.</summary>
    public static Task<Dictionary<string, VariantValue>> GetAll(DBusConnection connection, string destination, string path, string @interface)
        => connection.CallMethodAsync(
            Call(connection, destination, path, Properties, "GetAll", "s", w => w.WriteString(@interface)),
            static (Message reply, object? _) => reply.GetBodyReader().ReadDictionaryOfStringToVariantValue()
        );

    public static Task<VariantValue> Get(DBusConnection connection, string destination, string path, string @interface, string property)
        => connection.CallMethodAsync(
            Call(connection, destination, path, Properties, "Get", "ss", w =>
            {
                w.WriteString(@interface);
                w.WriteString(property);
            }),
            static (Message reply, object? _) => reply.GetBodyReader().ReadVariantValue()
        );

    public static Task Set(DBusConnection connection, string destination, string path, string @interface, string property, Action<MessageWriter> writeVariant)
        => connection.CallMethodAsync(Call(connection, destination, path, Properties, "Set", "ssv", w =>
        {
            w.WriteString(@interface);
            w.WriteString(property);
            writeVariant(w);
        }));

    /// <summary>A variant's value, looking through any variant wrapped around it.</summary>
    public static VariantValue Unwrap(this VariantValue value)
        => value.Type == VariantValueType.Variant ? value.GetVariantValue().Unwrap() : value;
}
