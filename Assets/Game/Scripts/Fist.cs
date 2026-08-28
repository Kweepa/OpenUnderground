using UnityEngine;

public class Fist : WeaponBase
{
    public Material[] skinMaterials;

    public override void Initialize(ushort[] objData, byte[] critterData)
    {
        base.Initialize(objData, critterData);

        MeshRenderer renderer = GetComponentInChildren<MeshRenderer>();
        if (renderer != null)
        {
            renderer.material = skinMaterials[PlayerData.sData.portrait == 1 ? 1 : 0];
        }
    }
}
