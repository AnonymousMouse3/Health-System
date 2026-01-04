using System;
using System.Collections.Generic;
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
        effect.damageOverTimeTask = Task.CompletedTask;

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
        
        if (effect.appliesDamageOverTime && effect.targetHealthSystem)
        {
            effect.damageOverTimeTask = DamageOverTime(effect);
        }

        if (effect.appliesStun)
        {
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
    
    private async Task DamageOverTime(PassiveEffectScriptableObject effect)
    {
        await MouseTools.AwaitableTimer(effect.damageTickDuration);
        if (effect.damageOverTimeTask == Task.CompletedTask) return;
        
        effect.targetHealthSystem.DoDamage(effect.damagePerTick);
        
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        DamageOverTime(effect);
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
