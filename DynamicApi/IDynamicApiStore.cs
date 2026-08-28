namespace StepFunctionsApp.DynamicApi;

/// <summary>Persistence for dynamic API definitions (one row per API in stepflow_data.db).</summary>
public interface IDynamicApiStore
{
    /// <summary>All active APIs whose NodePath equals the prefix or is nested under it ('/'-segment aware); null/empty = all. Sorted by Id.</summary>
    IReadOnlyList<DynamicApiDefinition> GetAll(string? nodePathPrefix = null);

    /// <summary>A single API by id (active or not), or null when missing.</summary>
    DynamicApiDefinition? GetById(string id);

    /// <summary>
    /// Upsert: an existing Id updates in place (preserving CreatedAt); else the same Name+NodePath reuses its Id;
    /// else a new uniquified Id is generated. Returns the stored Id and whether it was created.
    /// </summary>
    (string Id, bool Created) Save(DynamicApiDefinition definition);

    /// <summary>Deletes by id. False when missing.</summary>
    bool Delete(string id);
}
