using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyBox;
using UnityEngine;

[Serializable, CreateAssetMenu(fileName = "PassiveEffectScriptableObject", menuName = "Passive Effect")]
public class PassiveEffectScriptableObject : ScriptableObject
{
    [ReadOnly] public GameObject target; 
    [ReadOnly] public HealthSystem targetHealthSystem;


    public int id; // ENFORCE UNIQUE ID VIA EDITOR SCRIPT
    public float effectDuration;

    public DamageComponent damageComponent;
    
    public bool appliesStun;
    public bool appliesFear;

    public float moveSpeedModifier;
    public float horizontalKnockbackModifier;
    public float verticalKnockbackModifier;
    
    public float weaponDamageOutputModifier;
    public float spellDamageOutputModifier;
    
    public float damageVulnerabilityModifier;
    public DamageElement damageVulnerabilityElement;
    
    public List<ParticleSystem> particleEffects;

    public bool generatesEmbers;
    
    public enum DamageType
    {
        Projectile,
        Physical,
        Elemental,
    }
    
    public enum DamageElement
    {
        Fire,
        Water,
        Ice,
        Air,
        Iron,
        Lunar,
        Cosmic,
        Life,
        Blood,
    }

    public Task damageOverTimeTask;
    public CancellationTokenSource damageOverTimeCTS;
    //public VFX?? effectVFX;
}
