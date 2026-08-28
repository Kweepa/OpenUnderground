using UnityEngine;

public class Fountain : UUObject
{
    public AudioClip drinkSound;

    public override void TryInteract(UUObject originator, UUObject sender, EAction action)
    {
        if (action == EAction.Use)
        {
            // Play drink sound
            Utils.PlayClip2d(drinkSound);

            // refresh (restore health)
            if (isEnchanted)
            {
                // let the player know this is an enchanted fountain, even if they have full health
                PlayerObject.Player.RestoreHealth(PlayerData.sData.vitality);
                // take the mana up to half max, if it's lower than that
                int manaToRecover = PlayerData.sData.maxMana / 2 - PlayerData.sData.mana;
                if (manaToRecover > 0)
                {
                    Magic.sMagic.RestoreMana(manaToRecover);
                }
                PlayerData.sData.poison = 0;
                Messages.Add(1, 249);
                
                // Play GreaterHeal sound from Magic singleton
                if (Magic.sMagic != null && Magic.sMagic.castSpellSound != null)
                {
                    int greaterHealIndex = (int)Magic.ESpell.GreaterHeal;
                    if (greaterHealIndex < Magic.sMagic.castSpellSound.Length)
                    {
                        Utils.PlayClip2d(Magic.sMagic.castSpellSound[greaterHealIndex]);
                    }
                }

                ParticleSpawner.SpawnParticle(EParticleType.FountainMagic, transform.position);
            }
            else
            {
                // the water refreshes you
                Messages.Add(1, 237);
            }
        }
    }
}
