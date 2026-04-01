using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MouseLib;
using MyBox;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

public interface IDamageable
{
    void DoDamage(DamageComponent damageComponent);
    void DoDamageByNumber(float damage);
    
    void DoHealingByNumber(float healing);
}

public class HealthSystem : MonoBehaviour, IDamageable
{
    public delegate void OnHandleProjectileHit();
    public static OnHandleProjectileHit onHandleProjectileHit;
    
    public static event Action<GameObject, HealthSystem, DamageComponent> OnDamage; // Gameobject character that was damaged, their HealthSystem, attacking DamageComponent
    public static event Action<GameObject, HealthSystem, float> OnHeal;
    public static event Action<GameObject, GameObject> OnDeath;
    public static event Action<float, float> OnPlayerHealthChanged; // arg1 = currentHealth. arg2 = maxHealth.
    public static event Action<Image, float, float> OnSetBarFullPercent;
    
    public float MaxHealth
    {
        get => maxHealth;
        set
        {
            maxHealth = value;
            UpdateHealthbarUI();
        }
    }
    
    public float StartingHealth
    {
        get => startingHealth;
        set
        {
            startingHealth = value;
            UpdateHealthbarUI();
        }
    }

    public float CurrentHealth
    {
        get => currentHealth;
        set
        {
            currentHealth = value;
            UpdateHealthbarUI();
        }
    }

    public float LifestealHealth
    {
        get => lifestealHealth;
        set
        {
            lifestealHealth = value;
            UpdateHealthbarUI();
        }
    }

    [ReadOnly] public List<PassiveEffectScriptableObject> CurrentPassiveEffects => currentPassiveEffects;
    
    [Separator("Initialization")]
    [SerializeField] private bool initializeManually;
    
    [Separator("Health")]
    [SerializeField] private float maxHealth;
    [SerializeField] private float startingHealth;
    [SerializeField, ReadOnly] private float currentHealth;
    [SerializeField, ReadOnly] private float lifestealHealth;
    
    [Separator("Death")]
    [SerializeField, ReadOnly] private bool isDead;
    [SerializeField] private bool isInvulnerable;
    [SerializeField] private bool destroyOnDeath;
    [SerializeField] public bool isSanctuary;
    
    [Separator("Lifeleech")]
    [SerializeField] private bool canLeechLife;
    [SerializeField] private bool spawnAfterlifeOrb;
    [SerializeField, ReadOnly(nameof(spawnAfterlifeOrb), true)] private GameObject afterlifeObjectPrefab;
    [SerializeField] private bool isAfterlifeOrb;
    
    [Separator("Overheal")]
    [SerializeField] private bool canOverheal;
    [SerializeField, ReadOnly(nameof(canOverheal), true)] private float overhealMax; 
    [SerializeField, ReadOnly(nameof(canOverheal), true)] private float overhealDecayInterval;
    [SerializeField, ReadOnly(nameof(canOverheal), true)] private float overhealDecayIncrement;
    
    [Separator("Passive Effects")]
    [SerializeField, ReadOnly] private List<PassiveEffectScriptableObject> currentPassiveEffects;

    [Separator("Training Mode")]
    [SerializeField] private bool trainerMode = false;
    [SerializeField] private float timeTillHeal = 5;
    
    [Separator("Particles")]
    [SerializeField] ParticleSystem onDeathParticles;
    [SerializeField] ParticleSystem manastealParticles;
    [SerializeField] ParticleSystem lifestealParticles;
    
    [Separator("UI")]
    [SerializeField] Image healthbar;
    [SerializeField] Image lifestealHealthBar;
    
    private Task overhealDecayTask;
    private CancellationTokenSource overhealDecayCTS;
    
    private Task manastealQueueTask;
    private CancellationTokenSource manastealQueueCTS;
    private int burstSize;

    void OnEnable()
    {
        
    }

    void OnDisable()
    {
        
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (initializeManually) return;

        InitializeHealthSystem();

        effectManager = GameObject.Find("Game Manager").GetComponent<PassiveEffectManager>();
    }

    public void InitializeHealthSystem()
    {
        if (trainerMode) { StartCoroutine(TrainerMode()); }
        
        overhealDecayTask = Task.CompletedTask;
        currentHealth = startingHealth;
        isDead = false;

        if (canLeechLife)
        {
            lifestealHealth = 0;
        }

        if (!healthbar)
        {
            // Let the HUDManager know the player's max health at the start or log a warning if there is no health bar above the enemy.
            if (name != "Player")
            {
                Debug.LogWarning("Entity does not have health bar assigned and will cause exceptions. \n Please assign a health bar that will be show above their head or use the enemy prefab. \n Remove when all enemies are prefabs.", gameObject);
            }
            else
            {
                OnPlayerHealthChanged?.Invoke(currentHealth, maxHealth);
            }
        }
        else
        {
            OnSetBarFullPercent?.Invoke(healthbar, currentHealth, maxHealth);
            OnSetBarFullPercent?.Invoke(lifestealHealthBar, currentHealth, maxHealth);
        }
    }

    /// <summary>
    /// Updates the stealable aether bar when stolen.
    /// </summary>
    /// <param name="healthStolen">The amount of aether leached.</param>
    public void OnHealthLeeched(float healthHealed)
    {
        lifestealHealth = 0f;
        
        UpdateHealthbarUI();
        
        #if SPELL_SYSTEM
        SpawnLifestealParticles(healthHealed);
        #endif        

        if (!isAfterlifeOrb) return;
        Destroy(gameObject);
    }

    public void UpdateHealthbarUI()
    {
        // Update health bar.
        if (healthbar)
        {
            OnSetBarFullPercent?.Invoke(healthbar, currentHealth, maxHealth);
        }
        else if (name == "Player")
        {
            OnPlayerHealthChanged?.Invoke(currentHealth, maxHealth);
        }
        
        if (!canLeechLife) return;
        OnSetBarFullPercent?.Invoke(lifestealHealthBar, currentHealth + lifestealHealth, maxHealth);
    }

    public void DoDamage(DamageComponent damageComponent)
    {
        if (isInvulnerable) return;
        if (trainerMode){ timeTillHeal = 5; }
        
        #if SPELL_SYSTEM
        // only if spell
        if (damageComponent.enableLifesteal)
        {
            lifestealHealth += Mathf.Clamp(damageComponent.baseDamage, 0, currentHealth);
        }
        
        // only if gun
        if (damageComponent.enableManasteal)
        {
            QueueManastealParticles(damageComponent);
        }
        #endif
        
        currentHealth -= damageComponent.baseDamage;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        
        UpdateHealthbarUI();
        OnDamage?.Invoke(gameObject, this, damageComponent);
        
        if (currentHealth > 0) return;
        DoDeath();
    }
    
    public void DoDamageByNumber(float damage)
    {
        if (isInvulnerable) return;
        if (trainerMode){ timeTillHeal = 5; }
        
        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        
        UpdateHealthbarUI();
        
        if (currentHealth > 0) return;
        DoDeath();
    }

    public void DoHealingByNumber(float healing)
    {
        currentHealth += healing;

        currentHealth = Mathf.Clamp(currentHealth, 0, canOverheal ? overhealMax : maxHealth);

        // Update player health bar.
        if (!healthbar)
        {
            OnPlayerHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        if (currentHealth <= maxHealth) return;
        
        overhealDecayCTS?.Cancel();
        overhealDecayCTS = new CancellationTokenSource();
        overhealDecayTask = DecayOverheal(overhealDecayCTS.Token);
    }

    public void DoDeath()
    {
        if (isDead) return;
        if (isInvulnerable) return;
        if (trainerMode) return;
        if (isSanctuary){currentHealth = 1; return;}

        isDead = true;
        OnDeath?.Invoke(gameObject, gameObject);

        if (onDeathParticles)
        {
            Instantiate(onDeathParticles, transform.position, Quaternion.identity);
        }

        if (spawnAfterlifeOrb)
        {
            GameObject instance = Instantiate(afterlifeObjectPrefab, transform.position, Quaternion.identity);
            instance.TryGetComponent(out HealthSystem afterlifeHealthSystem);

            if (!afterlifeHealthSystem) return;
            afterlifeHealthSystem.MaxHealth = MaxHealth;
            afterlifeHealthSystem.InitializeHealthSystem();
            afterlifeHealthSystem.CurrentHealth = CurrentHealth;
            afterlifeHealthSystem.LifestealHealth = LifestealHealth;
        }

        if (!destroyOnDeath) return;
        gameObject.SetActive(false);
    }

    private async Task DecayOverheal(CancellationToken ct)
    {
        await MouseTools.AwaitableTimer(overhealDecayInterval);
        if (ct.IsCancellationRequested) return;
        
        if (currentHealth <= maxHealth) return;
        currentHealth -= overhealDecayIncrement;
        
        currentHealth = Mathf.Clamp(currentHealth, 0, overhealMax);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        overhealDecayCTS = new CancellationTokenSource();
        DecayOverheal(overhealDecayCTS.Token);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    }
    
    public IEnumerator TrainerMode()
    {
        if (trainerMode && timeTillHeal <= 0)
        {
            DoHealingByNumber(maxHealth);
            UpdateHealthbarUI();
            timeTillHeal = 5;
        }

        timeTillHeal -= 1;
        yield return new WaitForSeconds(1f);
        StartCoroutine(TrainerMode());
    }

    void Update()
    {
        isSanctuary = false;
    }

    public int CheckForPassive(int id)
    {
        for (int i = 0; i < currentPassiveEffects.Count; i++)
        {
            if (currentPassiveEffects[i].id == id)
            {
                return i;
            }
        }
        return -1;
    }

    private PassiveEffectManager effectManager;
    void OnCollisionStay(Collision other)
    {
        #if SPELL_SYSTEM
        if (other.gameObject.tag == "Liquid")
        {
            Liquid liquid = other.gameObject.GetComponent<LiquidV2Liquid>().liquid;

            if (liquid == Liquid.water)
            {
                if (CheckForPassive(1) != -1)
                {
                    effectManager.RemovePassiveEffect(CurrentPassiveEffects[CheckForPassive(1)]);
                }
            }
            if (liquid == Liquid.fire)
            {
                if (CheckForPassive(1) == -1)
                {
                    Debug.Log("A");
                    effectManager.AddPassiveEffect(effectManager.liquidBurn, this.gameObject);
                }
            }
        }
        #endif
    }
    
    #if SPELL_SYSTEM
    private void QueueManastealParticles(DamageComponent damageComponent)
    {
        if (!manastealParticles) return;

        int manaRestored = Mathf.Clamp(Mathf.RoundToInt(damageComponent.baseDamage * damageComponent.manastealMultiplier), 1, 99999);
        burstSize += manaRestored;
        
        manastealQueueCTS?.Cancel();
        manastealQueueCTS = new CancellationTokenSource();
        manastealQueueTask = SpawnManastealParticles(manastealQueueCTS.Token);
    }

    private async Task SpawnManastealParticles(CancellationToken ct)
    {
        if (!manastealParticles) return;
        
        await MouseTools.AwaitableTimer(0.05f);
        if (ct.IsCancellationRequested) return;
        
        manastealParticles.emission.SetBurst(0, new ParticleSystem.Burst(0.0f, burstSize, 1, 0.03f));
        manastealParticles.Play();
        burstSize = 0;
    }

    private void SpawnLifestealParticles(float healthHealed)
    {
        lifestealParticles.emission.SetBurst(0, new ParticleSystem.Burst(0.0f, healthHealed, 1, 0.03f));
        lifestealParticles.Play();
    }
    #endif
}
