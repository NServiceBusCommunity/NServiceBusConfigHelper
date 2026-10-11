public sealed record Choice<TValue>(TValue Value, string Title, string Description = "")
    where TValue : struct, Enum;
