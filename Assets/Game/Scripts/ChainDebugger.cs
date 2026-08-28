using UnityEngine;

public class ChainDebugger : MonoBehaviour
{
    public int objectToDebug;

    public void Update()
    {
        if (objectToDebug > 0)
        {
            bool chain = true;
            int objectInChain = objectToDebug;

            while (chain)
            {
                chain = false;
                for (int i = 0; i < 1024; ++i)
                {
                    UUObject obj = LevelLoader.GetObj(i);
                    if (obj != null)
                    {
                        if (obj.isLinked && obj.link == objectInChain)
                        {
                            Debug.Log($"{i} {obj.name}");
                            objectInChain = i;
                            chain = true;
                            break;
                        }
                    }
                }
            }
        }
    }
}
