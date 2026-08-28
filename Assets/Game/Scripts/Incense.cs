using UnityEngine;
using System.Collections;

public class Incense : UUObject
{
    public CutscenePlayer[] dreams;

    public void TryBurn()
    {
        // simplification = if you try to combine torch and incense, it just goes straight to the dreams
        // can sleep here reports if can't and why not
        if (Utils.CanSleepHere(notify: true))
        {
            PlayerObject.Player.StartCoroutine(IncenseUpdate());
        }
    }

    private IEnumerator IncenseUpdate()
    {
        PlayerObject.Player.fadeIn = false;

        while (PlayerObject.Player.fade < 1.0f)
        {
            PlayerObject.Player.fade += Time.unscaledDeltaTime;
            yield return null;
        }

        PlayerObject.Player.fade = 1.0f;

        PlayerData.sData.gameTime += 30 * 60; // 30 minutes

        // play one of the three cup dreams
        CutscenePlayer dream = Instantiate(dreams[PlayerData.sData.cupDreamIndex]);
        while (dream != null)
        {
            yield return null;
        }
        ++PlayerData.sData.cupDreamIndex;
        PlayerData.sData.cupDreamIndex %= dreams.Length;
        
        Utils.DestroyItem(this);

        while (PlayerObject.Player.fade > 0.0f)
        {
            PlayerObject.Player.fade -= Time.unscaledDeltaTime;
            yield return null;
        }
        PlayerObject.Player.fade = 0.0f;
    }
}
