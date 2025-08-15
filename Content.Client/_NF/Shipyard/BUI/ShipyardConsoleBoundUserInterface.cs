using Content.Client._NF.Shipyard.UI;
using Content.Shared.Containers.ItemSlots;
using Content.Shared._NF.Shipyard.BUI;
using Content.Shared._NF.Shipyard.Events;
using static Robust.Client.UserInterface.Controls.BaseButton;
using Robust.Client.UserInterface;
using Content.Client.Shuttles.Save;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.IoC;
using System.Linq;
using System.IO;

namespace Content.Client._NF.Shipyard.BUI;

public sealed class ShipyardConsoleBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly ShipFileManagementSystem _shipFileManagementSystem = default!;

    private ShipyardConsoleMenu? _menu;
    // private ShipyardRulesPopup? _rulesWindow; // Frontier
    public int Balance { get; private set; }

    public int? ShipSellValue { get; private set; }

    private Button? _loadShipButton; // Nullable, set in InitializeSaveLoadControls
    private ItemList? _savedShipsList; // Nullable, set in InitializeSaveLoadControls


    public ShipyardConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        if (_menu == null)
        {
            _menu = this.CreateWindow<ShipyardConsoleMenu>();
            _menu.OnOrderApproved += ApproveOrder;
            _menu.OnSellShip += SellShip;
            _menu.TargetIdButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent("ShipyardConsole-targetId"));

            // Disable the NFSD popup for now.
            // var rules = new FormattedMessage();
            // _rulesWindow = new ShipyardRulesPopup(this);
            // if (ShipyardConsoleUiKey.Security == (ShipyardConsoleUiKey) UiKey)
            // {
            //     rules.AddText(Loc.GetString($"shipyard-rules-default1"));
            //     rules.PushNewline();
            //     rules.AddText(Loc.GetString($"shipyard-rules-default2"));
            //     _rulesWindow.ShipRules.SetMessage(rules);
            //     _rulesWindow.OpenCentered();
            // }
        }
    }

    private void InitializeSaveLoadControls()
    {
        // This method would be called from your main Initialize method
        // after RobustXaml.Load(this) and other UI setup.

        if (_menu == null)
            return;

        _loadShipButton = _menu.FindControl<Button>("LoadShipButton"); // Get button from XAML
        _savedShipsList = _menu.FindControl<ItemList>("SavedShipsList"); // Get ItemList from XAML

        if (_loadShipButton != null)
            _loadShipButton.OnPressed += OnLoadShipButtonPressed;
        if (_savedShipsList != null)
            _savedShipsList.OnItemSelected += OnSavedShipSelected;

        RefreshSavedShipList();
    }

    private async void OnLoadShipButtonPressed(BaseButton.ButtonEventArgs args)
    {
        // Trigger file dialog to select a .yml file
        // This is a placeholder for actual file dialog implementation.
        // RobustToolbox has a way to open file dialogs, you'd use that here.
        var selectedFilePath = "/path/to/selected/ship.yml"; // Replace with actual file dialog result
        if (File.Exists(selectedFilePath))
        {
            await _shipFileManagementSystem.LoadShipFromFile(selectedFilePath);
        }
        else
        {
            Logger.Warning($"Selected file does not exist: {selectedFilePath}");
        }
    }

    private async void OnSavedShipSelected(ItemList.ItemListSelectedEventArgs args)
    {
        if (_savedShipsList == null)
            return;
        var selectedItem = _savedShipsList[args.ItemIndex];
        var filePath = (string)selectedItem.Metadata!;

        // You might want a confirmation dialog here before loading
        await _shipFileManagementSystem.LoadShipFromFile(filePath);
    }

    private void RefreshSavedShipList()
    {
        if (_savedShipsList == null)
            return;
        _savedShipsList.Clear();
        foreach (var filePath in _shipFileManagementSystem.GetSavedShipFiles())
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var item = _savedShipsList.AddItem(fileName);
            item.Metadata = filePath; // Store full path in metadata
        }
    }

    private void Populate(List<string> availablePrototypes, List<string> unavailablePrototypes, bool freeListings, bool validId)
    {
        if (_menu == null)
            return;

        _menu.PopulateProducts(availablePrototypes, unavailablePrototypes, freeListings, validId);
        _menu.PopulateCategories(availablePrototypes, unavailablePrototypes);
        _menu.PopulateClasses(availablePrototypes, unavailablePrototypes);
        _menu.PopulateEngines(availablePrototypes, unavailablePrototypes);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not ShipyardConsoleInterfaceState cState)
            return;

        Balance = cState.Balance;
        ShipSellValue = cState.ShipSellValue;
        var castState = (ShipyardConsoleInterfaceState) state;
        Populate(castState.ShipyardPrototypes.available, castState.ShipyardPrototypes.unavailable, castState.FreeListings, castState.IsTargetIdPresent);
        _menu?.UpdateState(castState);
    }

    private void ApproveOrder(ButtonEventArgs args)
    {
        if (args.Button.Parent?.Parent is not VesselRow row || row.Vessel == null)
        {
            return;
        }

        var vesselId = row.Vessel.ID;
        SendMessage(new ShipyardConsolePurchaseMessage(vesselId));
    }
    private void SellShip(ButtonEventArgs args)
    {
        //reserved for a sanity check, but im not sure what since we check all the important stuffs on server already
        SendMessage(new ShipyardConsoleSellMessage());
    }
}
