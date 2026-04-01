using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MouseLib;
using MyBox;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public class PassiveEffectManager : MonoBehaviour
{
    public delegate void OnAddPassiveEffect(PassiveEffectScriptableObject effect, GameObject target);
    public static OnAddPassiveEffect onAddPassiveEffect;
    
    public List<PassiveEffectScriptableObject> activePassiveEffects;
    public List<ParticleSystem> activeParticleEffects;

    [Header("Preset")]
    public PassiveEffectScriptableObject liquidBurn;

    private void OnEnable()
    {
        onAddPassiveEffect += AddPassiveEffect;
    }

    private void OnDisable()
    {
        onAddPassiveEffect -= AddPassiveEffect;
    }

    public void AddPassiveEffect(PassiveEffectScriptableObject effect, GameObject target)
    {
        if (effect.id == 0) { Debug.Log("PASSIVE EFFECT ID NOT SET. SET IN INSPECTOR."); return; }
        
        effect = Instantiate(effect);
        effect.target = target;
        activePassiveEffects.Add(effect);
        
        InitializePassiveEffect(effect);
    }

    public void RemovePassiveEffect(PassiveEffectScriptableObject effect)
    {
        activePassiveEffects.Remove(effect);
        effect.damageOverTimeCTS?.Cancel();

        foreach (ParticleSystem particleEffect in activeParticleEffects)
        {
            if (!particleEffect) continue;
            particleEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        
        if (!effect.targetHealthSystem) return;
        effect.targetHealthSystem.CurrentPassiveEffects.Remove(effect);
    }

    private void InitializePassiveEffect(PassiveEffectScriptableObject effect)
    {
        effect.target.TryGetComponent(out HealthSystem targetHealthSystem);
        effect.targetHealthSystem = targetHealthSystem;
        
        // Refresh cooldown or add stacks if the effect is already active on this target
        foreach (PassiveEffectScriptableObject passiveEffect in effect.targetHealthSystem.CurrentPassiveEffects)
        {
            if (passiveEffect.id != effect.id) continue;
            RemovePassiveEffect(passiveEffect);
            break;
        }
        
        if (effect.targetHealthSystem)
        {
            targetHealthSystem.CurrentPassiveEffects.Add(effect);
        }
        
        if (effect.damageComponent.appliesDamageOverTime && effect.targetHealthSystem)
        {
            effect.damageOverTimeCTS = new CancellationTokenSource();
            effect.damageOverTimeTask = DamageOverTime(effect.damageOverTimeCTS.Token, effect);
        }

        if (effect.appliesStun)
        {
            effect.target.TryGetComponent(out MovementSystem targetMovementSystem);
            
            ApplyStun();
        }

        StartParticleEffect(effect);
        EffectTimer(effect, effect.effectDuration);
    }

    private async void EffectTimer(PassiveEffectScriptableObject effect, float stopAfterTime = 0f)
    {
        await MouseTools.AwaitableTimer(stopAfterTime);
        
        RemovePassiveEffect(effect);
    }
    
    private async Task DamageOverTime(CancellationToken ct, PassiveEffectScriptableObject effect)
    {
        await MouseTools.AwaitableTimer(effect.damageComponent.damageTickDuration);
        if (ct.IsCancellationRequested) return;
        
        // this uses simple damage and isn't compatible with manasteal or lifesteal
        effect.targetHealthSystem.DoDamageByNumber(effect.damageComponent.damagePerTick);
        
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        effect.damageOverTimeCTS = new CancellationTokenSource();
        DamageOverTime(effect.damageOverTimeCTS.Token, effect);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    }

    private void ApplyStun()
    {
        
    }

    private void StartParticleEffect(PassiveEffectScriptableObject effect)
    {
        foreach (ParticleSystem particleEffect in effect.particleEffects)
        {
            ParticleSystem instance = Instantiate(particleEffect, effect.target.transform);
            activeParticleEffects.Add(instance);
        }
    }
}
