using Content.Shared._Hardlight.Drugs;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Hardlight.Drugs;

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

        // Check if solution is at full capacity
        if (solution.Volume < solution.MaxVolume)
            return false;

        // Check if it's a single valid drug
        if (solution.Contents.Count != 1)
            return false;

        var reagent = solution.Contents[0];
        return GetWrappedPacketId(reagent.Reagent.Prototype) != null;
    }

    private void SealPacket(Entity<PacketSealingComponent> ent, EntityUid user)
    {
        if (!_solution.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var solutionEnt, out var solution))
            return;

        if (solution.Contents.Count != 1)
            return;

        var reagent = solution.Contents[0];
        var wrappedPacketId = GetWrappedPacketId(reagent.Reagent.Prototype);
        if (wrappedPacketId == null)
            return;

        var wrappedPacket = Spawn(wrappedPacketId, Transform(ent.Owner).Coordinates);
        
        // Play seal sound at the packet location for all players
        if (ent.Comp.SealSound != null)
            _audio.PlayPvs(ent.Comp.SealSound, ent.Owner);

        // Remove the empty packet
        Del(ent.Owner);
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