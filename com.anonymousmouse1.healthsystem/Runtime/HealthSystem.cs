using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using MouseLib;
using MyBox;
using UnityEngine;
using UnityEngine.UI;

public interface IDamageable
{
    void DoDamage(float damage);
    
    void DoHealing(float healing);
}

public class HealthSystem : MonoBehaviour, IDamageable
{
    public delegate void OnHandleProjectileHit();
    public static OnHandleProjectileHit onHandleProjectileHit;
    
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
    
    [Separator("UI")]
    [SerializeField] Image healthbar;
    [SerializeField] Image lifestealHealthBar;
    
    private Task overhealDecayTask;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (initializeManually) return;

        InitializeHealthSystem();
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
    public void OnHealthLeeched()
    {
        lifestealHealth = 0f;
        
       UpdateHealthbarUI();
        
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

    public void DoDamage(float damage)
    {
        if (isInvulnerable) return;
        if (trainerMode){ timeTillHeal = 5; }
        
        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

        lifestealHealth += damage;
        
        UpdateHealthbarUI();
        
        if (currentHealth > 0) return;
        DoDeath();
    }

    public void DoHealing(float healing)
    {
        currentHealth += healing;

        currentHealth = Mathf.Clamp(currentHealth, 0, canOverheal ? overhealMax : maxHealth);

        // Update player health bar.
        if (!healthbar)
        {
            OnPlayerHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        if (currentHealth <= maxHealth) return;
        if (overhealDecayTask != Task.CompletedTask) return;
        
        overhealDecayTask = DecayOverheal();
    }

    public void DoDeath()
    {
        if (isDead) return;
        if (isInvulnerable) return;
        if (trainerMode) return;

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

    private async Task DecayOverheal()
    {
        await MouseTools.AwaitableTimer(overhealDecayInterval);
        
        if (currentHealth <= maxHealth) return;
        currentHealth -= overhealDecayIncrement;
        
        currentHealth = Mathf.Clamp(currentHealth, 0, overhealMax);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        DecayOverheal();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    }
    
    public IEnumerator TrainerMode()
    {
        if (trainerMode && timeTillHeal <= 0)
        {
            DoHealing(maxHealth);
            UpdateHealthbarUI();
            timeTillHeal = 5;
        }

        timeTillHeal -= 1;
        yield return new WaitForSeconds(1f);
        StartCoroutine(TrainerMode());
    }
}
