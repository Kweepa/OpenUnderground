using UnityEngine;
using System.Collections.Generic;

public class Shelf : UUObject
{
    public override string GetLookName()
    {
        return "a shelf";
    }

    public static void PutBooksOnShelves(UUObject[] levelObjects, UUObject shelf)
    {
        // find islands of books
        List<List<Book>> bookIslands = new List<List<Book>>();
        foreach (UUObject obj in levelObjects)
        {
            Book book = obj as Book;
            if (book != null && book.enabled)
            {
                // try to add to a current island
                foreach (List<Book> island in bookIslands)
                {
                    foreach (Book other in island)
                    {
                        if ((other.transform.position - book.transform.position).sqrMagnitude < 25.0f)
                        {
                            island.Add(book);
                            book = null;
                            break;
                        }
                    }
                    if (book == null)
                    {
                        break;
                    }
                }
                if (book != null)
                {
                    // start a new island
                    List<Book> newIsland = new List<Book>();
                    newIsland.Add(book);
                    bookIslands.Add(newIsland);
                }
            }
        }

        foreach (List<Book> bookIsland in bookIslands)
        {
            if (bookIsland.Count >= 3)
            {
                // find the center, then find a wall to put the shelf on
                Vector3 center = Vector3.zero;
                foreach (Book book in bookIsland)
                {
                    center += book.transform.position;
                }
                center /= bookIsland.Count;

                List<Vector3> dirs = new List<Vector3>() { Vector3.back, Vector3.left, Vector3.forward, Vector3.right };
                Vector3 startPos = center + 1.3f * Vector3.up;
                foreach (Vector3 dir in dirs)
                {
                    int layerMask = LayerMasks.EnvironmentAndCeiling;
                    RaycastHit hit;
                    if (Physics.Raycast(startPos, dir, out hit, 3.0f, layerMask))
                    {
                        // put shelf here then put the books on it
                        UUObject shelfObj = Instantiate(shelf, hit.point, Quaternion.LookRotation(hit.normal));
                        shelfObj.objectIndex = 0;
                        shelfObj.originalLevel = 0;
                        shelfObj.levelIndex = LevelLoader.sLevelLoader.loadedLevel;
                        LevelLoader.AddToWorld(shelfObj);

                        Vector3 bookPos = hit.point - 0.2f * shelfObj.transform.right + 0.3f * hit.normal; 
                        foreach (Book book in bookIsland)
                        {
                            // TODO: give book transform more randomness
                            // then maybe rotate the last book to lean against the others
                            book.transform.position = bookPos;
                            book.transform.rotation = Quaternion.LookRotation(-hit.normal);
                            bookPos += Random.Range(0.1f, 0.3f) * shelfObj.transform.right;
                        }
                        break;
                    }
                }
            }
        }
    }
}
