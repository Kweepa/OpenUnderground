[System.Serializable]
public class TutorialSaveData
{
    public const int CurrentVersion = 1;

    public int version;
    public int completedMask;
    public double gameplayStartTime;
    public double firstPickupTime = -1.0;
    public bool hasOpenedInventory;
    public int runesStowedCount;
    public float movementAccumulatedDisplayTime;
}

public enum ETutorialStep
{
    Movement = 0,
    PortablePickup = 1,
    PortableDropInInventory = 2,
    Inventory = 3,
    WeaponCharge = 4,
    MagicPanelOpen = 5,
    MagicCast = 6,
    Interactable = 7,
    StatsPanel = 8,
    Count = 9
}
