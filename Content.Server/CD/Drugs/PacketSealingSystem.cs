using Content.Shared.CD.Drugs;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio.Systems;

namespace Content.Server.CD.Drugs;

/// <summary>
/// Server-side system that handles the actual packet transformation
/// </summary>
public sealed class PacketSealingSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;

    public override void Initialize()
    {
        base.Initialize();
        
        SubscribeLocalEvent<PacketSealingComponent, PacketSealDoAfterEvent>(OnSealDoAfter);
    }

    private void OnSealDoAfter(Entity<PacketSealingComponent> ent, ref PacketSealDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        // Validate we can still seal (reagent check)
        if (!CanSealPacket(ent))
            return;

        SealPacket(ent, args.User);
        args.Handled = true;
    }

    private bool CanSealPacket(Entity<PacketSealingComponent> ent)
    {
        if (ent.Comp.State == PacketState.Sealed)
            return false;

        if (!_solution.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var solutionEnt, out var solution))
            return false;

        // Check if any single reagent has enough quantity to seal
        foreach (var reagent in solution.Contents)
        {
            if (reagent.Quantity >= ent.Comp.MinReagentAmount && GetWrappedPacketId(reagent.Reagent.Prototype) != null)
                return true;
        }

        return false;
    }

    private void SealPacket(Entity<PacketSealingComponent> ent, EntityUid user)
    {
        if (!_solution.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var solutionEnt, out var solution))
            return;

        // Find the primary reagent to determine packet type
        var primaryReagent = GetPrimaryReagent(solution, ent.Comp.MinReagentAmount);
        if (primaryReagent == null)
            return;

        // Transform the empty packet into the appropriate wrapped packet
        var wrappedPacketId = GetWrappedPacketId(primaryReagent);
        if (wrappedPacketId == null)
            return;

        var wrappedPacket = Spawn(wrappedPacketId, Transform(ent.Owner).Coordinates);
        
        // Play seal sound
        if (ent.Comp.SealSound != null)
            _audio.PlayPvs(ent.Comp.SealSound, ent.Owner);

        // Remove the empty packet
        Del(ent.Owner);
    }

    private string? GetPrimaryReagent(Solution solution, FixedPoint2 minQuantity)
    {
        string? primaryReagent = null;
        var highestQuantity = FixedPoint2.Zero;

        foreach (var reagent in solution.Contents)
        {
            // Only consider reagents that meet minimum quantity and are valid drugs
            if (reagent.Quantity >= minQuantity && 
                reagent.Quantity > highestQuantity && 
                GetWrappedPacketId(reagent.Reagent.Prototype) != null)
            {
                highestQuantity = reagent.Quantity;
                primaryReagent = reagent.Reagent.Prototype;
            }
        }

        return primaryReagent;
    }

    private string? GetWrappedPacketId(string reagentId)
    {
        return reagentId switch
        {
            "Bake" => "WrappedBakePackage",
            "Rust" => "WrappedRustPackage", 
            "Grit" => "WrappedGritPackage",
            "Breakout" => "WrappedBreakoutPackage",
            _ => null
        };
    }
}