using System.Collections.Generic;
using UnityEngine;

public class ObjectCombination
{
    public ObjectCombination(Stream stream)
    {
        object1 = stream.GetShort();
        object2 = stream.GetShort();
        result = stream.GetShort();
    }

    private ObjectCombination(ObjectCombination comb, bool flip)
    {
        if (flip)
        {
            object1 = comb.object2;
            object2 = comb.object1;
            result = comb.result;
        }
    }
    
    public ObjectCombination GetFlip()
    {
        return new ObjectCombination(this, true);
    }

    public int GetObject1() { return object1 & 511; }
    public int GetObject2() { return object2 & 511; }
    public int GetResult() { return result & 511; }

    public bool Object1Destroyed() { return (object1 & ~511) != 0; }
    public bool Object2Destroyed() { return (object2 & ~511) != 0; }

    public void PrintCombination()
    {
        string name1 = StringLoader.GetString(4, GetObject1());
        string name2 = StringLoader.GetString(4, GetObject2());
        string name3 = StringLoader.GetString(4, GetResult());
        string destroy1 = Object1Destroyed() ? "*" : "";
        string destroy2 = Object2Destroyed() ? "*" : "";
        Debug.Log($"{name1}{destroy1} + {name2}{destroy2} = {name3}"); 
    }
    
    private readonly short object1;
    private readonly short object2;
    private readonly short result;
}

public class CombineObjects
{
    public static List<ObjectCombination> GetAllCombinations(string filename)
    {
        Stream stream = new Stream(filename);
        int num = stream.GetLength() / 6;
        List<ObjectCombination> combs = new List<ObjectCombination>();
        for (int i = 0; i < num; ++i)
        {
            ObjectCombination c = new ObjectCombination(stream);
            if (c.GetResult() != 0)
            {
                //c.PrintCombination();
                combs.Add(c);
            }
        }
        return combs;
    }

    public static ObjectCombination FindCombination(UUObject obj1, UUObject obj2)
    {
        foreach (ObjectCombination comb in DataLoader.sDataLoader.objCombinations)
        {
            if (comb.GetObject1() == obj1.GetCombinationType() && comb.GetObject2() == obj2.GetCombinationType())
            {
                return comb;
            }

            if (comb.GetObject1() == obj2.GetCombinationType() && comb.GetObject2() == obj1.GetCombinationType())
            {
                return comb.GetFlip();
            }
        }

        return null;
    }
}
