namespace Catnip.Core.Modules;

public static class ModuleIds
{
    public const string TodayTodos = "today-todos";
    public const string CustomerInteractions = "customer-interactions";
    public const string CustomerWriteback = "customer-writeback";
    public const string Weather = "weather";

    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly([TodayTodos, CustomerInteractions, CustomerWriteback, Weather]);
}
