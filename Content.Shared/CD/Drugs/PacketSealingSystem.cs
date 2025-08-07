using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.CD.Drugs;

/// <summary>
/// System that handles packet sealing via right-click activation
/// </summary>
public sealed class PacketSealingSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;

    public override void Initialize()
    {
        base.Initialize();
        
        SubscribeLocalEvent<PacketSealingComponent, ActivateInWorldEvent>(OnActivated);
        SubscribeLocalEvent<PacketSealingComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerbs);
    }

    private void OnActivated(Entity<PacketSealingComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex || ent.Comp.State == PacketState.Sealed)
            return;

        if (CanSealPacket(ent))
        {
            TryStartSealDoAfter(ent, args.User);
            args.Handled = true;
        }
    }

    private void OnGetAltVerbs(Entity<PacketSealingComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        if (ent.Comp.State == PacketState.Sealed || !CanSealPacket(ent))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb()
        {
            Text = "Seal Packet",
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/lock.svg.192dpi.png")),
            Act = () => TryStartSealDoAfter(ent, user),
        });
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

    private void TryStartSealDoAfter(Entity<PacketSealingComponent> ent, EntityUid user)
    {
        if (!CanSealPacket(ent))
            return;

        var doAfterArgs = new DoAfterArgs(EntityManager, user, ent.Comp.SealDelay, new PacketSealDoAfterEvent(), ent.Owner)
        {
            BreakOnMove = true,
            NeedHand = true,
            BreakOnHandChange = true,
            MovementThreshold = 0.01f,
            DistanceThreshold = 1.0f,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnSealDoAfter(Entity<PacketSealingComponent> ent, ref PacketSealDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || !CanSealPacket(ent))
            return;

        // Mark as sealed to prevent further sealing attempts
        ent.Comp.State = PacketState.Sealed;
        Dirty(ent.Owner, ent.Comp);
        
        args.Handled = true;
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

[Serializable, NetSerializable]
public sealed partial class PacketSealDoAfterEvent : SimpleDoAfterEvent
{
}