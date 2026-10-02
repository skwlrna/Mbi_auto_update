namespace FishingAutomation;

internal enum ProductionStage
{
    Idle,
    OpenHub,
    SelectCategory,
    Search,
    Detail,
    CreateQuest,
    ReadQuest,
    AcquireMaterial,
    ResolveIntermediate,
    Travel,
    Process,
    VerifyInventory,
    Complete
}

/// <summary>
/// Small explicit state machine shared by the production automations. It does
/// not click anything; it prevents hidden/implicit stage jumps and gives the UI
/// one authoritative description of what the engine believes it is doing.
/// </summary>
internal sealed class ProductionStageMachine
{
    private readonly string _mode;
    internal ProductionStage Current { get; private set; } = ProductionStage.Idle;
    internal event Action<ProductionStage, string>? Changed;

    internal ProductionStageMachine(string mode) => _mode = mode;

    internal void Move(ProductionStage next, string detail)
    {
        if (!Allowed(Current, next))
            throw new InvalidOperationException(
                $"[{_mode}] 생산 단계 전이가 올바르지 않습니다: {Current} -> {next} · {detail}");

        Current = next;
        Changed?.Invoke(next, detail);
    }

    internal void Reset(string detail = "대기")
    {
        Current = ProductionStage.Idle;
        Changed?.Invoke(Current, detail);
    }

    private static bool Allowed(ProductionStage from, ProductionStage to)
    {
        if (to == ProductionStage.Idle)
            return true;
        if (to == ProductionStage.OpenHub)
            return true; // bounded recovery/navigation may return to a known hub from any stage.
        if (from == to)
            return true;

        return from switch
        {
            ProductionStage.Idle =>
                to is ProductionStage.OpenHub or ProductionStage.AcquireMaterial,
            ProductionStage.OpenHub =>
                to is ProductionStage.SelectCategory or ProductionStage.Search or ProductionStage.Detail or
                    ProductionStage.Travel or ProductionStage.Process,
            ProductionStage.SelectCategory =>
                to is ProductionStage.Search or ProductionStage.Detail,
            ProductionStage.Search =>
                to is ProductionStage.Detail,
            ProductionStage.Detail =>
                to is ProductionStage.CreateQuest or ProductionStage.Process or
                    ProductionStage.Travel or ProductionStage.VerifyInventory,
            ProductionStage.CreateQuest =>
                to is ProductionStage.ReadQuest or ProductionStage.VerifyInventory,
            ProductionStage.ReadQuest =>
                to is ProductionStage.AcquireMaterial or ProductionStage.ResolveIntermediate or
                    ProductionStage.Travel or ProductionStage.Process or ProductionStage.VerifyInventory,
            ProductionStage.AcquireMaterial =>
                to is ProductionStage.ReadQuest or ProductionStage.VerifyInventory or
                    ProductionStage.ResolveIntermediate or ProductionStage.Travel,
            ProductionStage.ResolveIntermediate =>
                to is ProductionStage.ReadQuest or ProductionStage.AcquireMaterial or
                    ProductionStage.Process or ProductionStage.VerifyInventory,
            ProductionStage.Travel =>
                to is ProductionStage.Detail or ProductionStage.Process or
                    ProductionStage.VerifyInventory,
            ProductionStage.Process =>
                to is ProductionStage.VerifyInventory or ProductionStage.Complete,
            ProductionStage.VerifyInventory =>
                to is ProductionStage.OpenHub or ProductionStage.ReadQuest or
                    ProductionStage.AcquireMaterial or ProductionStage.ResolveIntermediate or
                    ProductionStage.Travel or ProductionStage.Process or ProductionStage.Complete,
            ProductionStage.Complete =>
                to is ProductionStage.VerifyInventory or ProductionStage.OpenHub or ProductionStage.Idle,
            _ => false
        };
    }
}
