using UnityEngine;

public class Weapon : WeaponBase
{
    private Animator cachedAnimator;
    private string currentAnimation = "";

    protected void Awake()
    {
        cachedAnimator = gameObject.GetComponentInChildren<Animator>();
    }

    public override void Update()
    {
        base.Update();
        
        // since the animator doesn't behave correctly when just enabled, need to poll this
        if (state == EState.Hold
            && cachedAnimator != null && !cachedAnimator.GetCurrentAnimatorStateInfo(0).IsName("Hold"))
        {
            currentAnimation = "";
            ChangeAnimation("Hold");
        }
    }
    
    private void ChangeAnimation(string animationName)
    {
        if (cachedAnimator != null && animationName != currentAnimation)
        {
            if (animationName == "Reset")
            {
                cachedAnimator.Play(animationName);
            }
            else
            {
                cachedAnimator.CrossFade(animationName, 0.1f);
            }
            currentAnimation = animationName;
        }
    }

    protected override void ChangeState(EState newState)
    {
        EState oldState = state;
        base.ChangeState(newState);

        if (newState != oldState && oldState == EState.Reset)
        {
            if (cachedAnimator != null)
            {
                cachedAnimator.enabled = true;
            }
        }

        string enumString = newState.ToString();
        string enumName = enumString.Substring(enumString.LastIndexOf('.') + 1);
        ChangeAnimation(enumName);

        if (newState != oldState)
        {
            if (newState == EState.Reset)
            {
                if (cachedAnimator != null)
                {
                    cachedAnimator.enabled = false;
                }
            }
        }
    }
}