using Full.NET.Data.Abstractions;

namespace Full.NET.Modules.Identity.Persistence;

internal static class IdentityOidcSql
{
    private const string ApplicationColumns = """
        app.Id, app.ClientId, app.ClientSecret, app.ConsentType, app.DisplayName, app.DisplayNamesJson,
        app.PermissionsJson, app.PostLogoutRedirectUrisJson, app.PropertiesJson, app.RedirectUrisJson,
        app.RequirementsJson, app.ApplicationType, app.JsonWebKeySetJson, app.SettingsJson, app.ClientType,
        app.Version, app.CreatedAtUtc, app.UpdatedAtUtc
        """;

    private const string AuthorizationColumns = """
        auth.Id, auth.ApplicationId, auth.CreationDateUtc, auth.PropertiesJson, auth.ScopesJson,
        auth.Status, auth.Subject, auth.Type, auth.Version, auth.CreatedAtUtc, auth.UpdatedAtUtc
        """;

    private const string AuthorizationDetailColumns = """
        auth.Id, auth.ApplicationId, auth.CreationDateUtc, auth.PropertiesJson, auth.ScopesJson,
        auth.Status, auth.Subject, auth.Type, auth.Version, auth.CreatedAtUtc, auth.UpdatedAtUtc,
        app.ClientId
        """;

    private const string ScopeColumns = """
        scope.Id, scope.Name, scope.Description, scope.DescriptionsJson, scope.DisplayName,
        scope.DisplayNamesJson, scope.PropertiesJson, scope.ResourcesJson, scope.Version,
        scope.CreatedAtUtc, scope.UpdatedAtUtc
        """;

    private const string TokenColumns = """
        token.Id, token.ApplicationId, token.AuthorizationId, token.CreationDateUtc, token.ExpirationDateUtc,
        token.Payload, token.PropertiesJson, token.RedemptionDateUtc, token.ReferenceId, token.Status,
        token.Subject, token.Type, token.Version, token.CreatedAtUtc, token.UpdatedAtUtc
        """;

    public static readonly SqlStatement InsertApplication = new(
        "identity.insert_oidc_application",
        """
        INSERT INTO fn_identity_oidc_application
            (Id, ClientId, ClientSecret, ConsentType, DisplayName, DisplayNamesJson, PermissionsJson,
             PostLogoutRedirectUrisJson, PropertiesJson, RedirectUrisJson, RequirementsJson, ApplicationType,
             JsonWebKeySetJson, SettingsJson, ClientType, Version, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @ClientId, @ClientSecret, @ConsentType, @DisplayName, @DisplayNamesJson, @PermissionsJson,
             @PostLogoutRedirectUrisJson, @PropertiesJson, @RedirectUrisJson, @RequirementsJson, @ApplicationType,
             @JsonWebKeySetJson, @SettingsJson, @ClientType, @Version, @CreatedAtUtc, @UpdatedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateApplication = new(
        "identity.update_oidc_application",
        """
        UPDATE fn_identity_oidc_application
        SET ClientId = @ClientId, ClientSecret = @ClientSecret, ConsentType = @ConsentType,
            DisplayName = @DisplayName, DisplayNamesJson = @DisplayNamesJson, PermissionsJson = @PermissionsJson,
            PostLogoutRedirectUrisJson = @PostLogoutRedirectUrisJson, PropertiesJson = @PropertiesJson,
            RedirectUrisJson = @RedirectUrisJson, RequirementsJson = @RequirementsJson,
            ApplicationType = @ApplicationType, JsonWebKeySetJson = @JsonWebKeySetJson,
            SettingsJson = @SettingsJson, ClientType = @ClientType, UpdatedAtUtc = @UpdatedAtUtc,
            Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteApplication = new(
        "identity.delete_oidc_application",
        "DELETE FROM fn_identity_oidc_application WHERE Id = @Id AND Version = @Version",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindApplicationById = new(
        "identity.find_oidc_application_by_id",
        $"SELECT {ApplicationColumns} FROM fn_identity_oidc_application AS app WHERE app.Id = @Id",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindApplicationByClientId = new(
        "identity.find_oidc_application_by_client_id",
        $"SELECT {ApplicationColumns} FROM fn_identity_oidc_application AS app WHERE app.ClientId = @ClientId",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountApplications = new(
        "identity.count_oidc_applications",
        "SELECT COUNT(1) AS Count FROM fn_identity_oidc_application",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListApplicationsSqlServer = new(
        "identity.list_oidc_applications.sql_server",
        $"""
        SELECT {ApplicationColumns}
        FROM fn_identity_oidc_application AS app
        ORDER BY app.CreatedAtUtc DESC, app.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListApplicationsMySql = new(
        "identity.list_oidc_applications.mysql",
        $"""
        SELECT {ApplicationColumns}
        FROM fn_identity_oidc_application AS app
        ORDER BY app.CreatedAtUtc DESC, app.Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountApplicationsFilteredSqlServer = new(
        "identity.count_oidc_applications_filtered.sql_server",
        """
        SELECT COUNT(1) AS Count
        FROM fn_identity_oidc_application AS app
        WHERE (@ClientIdContains IS NULL OR app.ClientId LIKE '%' + @ClientIdContains + '%')
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountApplicationsFilteredMySql = new(
        "identity.count_oidc_applications_filtered.mysql",
        """
        SELECT COUNT(1) AS Count
        FROM fn_identity_oidc_application AS app
        WHERE (@ClientIdContains IS NULL OR app.ClientId LIKE CONCAT('%', @ClientIdContains, '%'))
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListApplicationsFilteredSqlServer = new(
        "identity.list_oidc_applications_filtered.sql_server",
        $"""
        SELECT {ApplicationColumns}
        FROM fn_identity_oidc_application AS app
        WHERE (@ClientIdContains IS NULL OR app.ClientId LIKE '%' + @ClientIdContains + '%')
        ORDER BY app.CreatedAtUtc DESC, app.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListApplicationsFilteredMySql = new(
        "identity.list_oidc_applications_filtered.mysql",
        $"""
        SELECT {ApplicationColumns}
        FROM fn_identity_oidc_application AS app
        WHERE (@ClientIdContains IS NULL OR app.ClientId LIKE CONCAT('%', @ClientIdContains, '%'))
        ORDER BY app.CreatedAtUtc DESC, app.Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindApplicationsByRedirectUriSqlServer = new(
        "identity.find_oidc_applications_by_redirect_uri.sql_server",
        $"""
        SELECT {ApplicationColumns}
        FROM fn_identity_oidc_application AS app
        WHERE app.RedirectUrisJson IS NOT NULL
          AND EXISTS (SELECT 1 FROM OPENJSON(app.RedirectUrisJson) WHERE [value] = @Uri)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindApplicationsByRedirectUriMySql = new(
        "identity.find_oidc_applications_by_redirect_uri.mysql",
        $"""
        SELECT {ApplicationColumns}
        FROM fn_identity_oidc_application AS app
        WHERE app.RedirectUrisJson IS NOT NULL
          AND JSON_CONTAINS(app.RedirectUrisJson, JSON_QUOTE(@Uri))
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindApplicationsByPostLogoutRedirectUriSqlServer = new(
        "identity.find_oidc_applications_by_post_logout_redirect_uri.sql_server",
        $"""
        SELECT {ApplicationColumns}
        FROM fn_identity_oidc_application AS app
        WHERE app.PostLogoutRedirectUrisJson IS NOT NULL
          AND EXISTS (SELECT 1 FROM OPENJSON(app.PostLogoutRedirectUrisJson) WHERE [value] = @Uri)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindApplicationsByPostLogoutRedirectUriMySql = new(
        "identity.find_oidc_applications_by_post_logout_redirect_uri.mysql",
        $"""
        SELECT {ApplicationColumns}
        FROM fn_identity_oidc_application AS app
        WHERE app.PostLogoutRedirectUrisJson IS NOT NULL
          AND JSON_CONTAINS(app.PostLogoutRedirectUrisJson, JSON_QUOTE(@Uri))
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertAuthorization = new(
        "identity.insert_oidc_authorization",
        """
        INSERT INTO fn_identity_oidc_authorization
            (Id, ApplicationId, CreationDateUtc, PropertiesJson, ScopesJson, Status, Subject, Type,
             Version, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @ApplicationId, @CreationDateUtc, @PropertiesJson, @ScopesJson, @Status, @Subject, @Type,
             @Version, @CreatedAtUtc, @UpdatedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateAuthorization = new(
        "identity.update_oidc_authorization",
        """
        UPDATE fn_identity_oidc_authorization
        SET ApplicationId = @ApplicationId, CreationDateUtc = @CreationDateUtc, PropertiesJson = @PropertiesJson,
            ScopesJson = @ScopesJson, Status = @Status, Subject = @Subject, Type = @Type,
            UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteAuthorization = new(
        "identity.delete_oidc_authorization",
        "DELETE FROM fn_identity_oidc_authorization WHERE Id = @Id AND Version = @Version",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindAuthorizationById = new(
        "identity.find_oidc_authorization_by_id",
        $"SELECT {AuthorizationColumns} FROM fn_identity_oidc_authorization AS auth WHERE auth.Id = @Id",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindAuthorizationDetailById = new(
        "identity.find_oidc_authorization_detail_by_id",
        $"""
        SELECT {AuthorizationDetailColumns}
        FROM fn_identity_oidc_authorization AS auth
        LEFT JOIN fn_identity_oidc_application AS app ON app.Id = auth.ApplicationId
        WHERE auth.Id = @Id
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountAuthorizationsFilteredSqlServer = new(
        "identity.count_oidc_authorizations_filtered.sql_server",
        """
        SELECT COUNT(1) AS Count
        FROM fn_identity_oidc_authorization AS auth
        LEFT JOIN fn_identity_oidc_application AS app ON app.Id = auth.ApplicationId
        WHERE (@ApplicationId IS NULL OR auth.ApplicationId = @ApplicationId)
          AND (@Subject IS NULL OR auth.Subject = @Subject)
          AND (@Status IS NULL OR auth.Status = @Status)
          AND (@ClientIdContains IS NULL OR app.ClientId LIKE '%' + @ClientIdContains + '%')
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountAuthorizationsFilteredMySql = new(
        "identity.count_oidc_authorizations_filtered.mysql",
        """
        SELECT COUNT(1) AS Count
        FROM fn_identity_oidc_authorization AS auth
        LEFT JOIN fn_identity_oidc_application AS app ON app.Id = auth.ApplicationId
        WHERE (@ApplicationId IS NULL OR auth.ApplicationId = @ApplicationId)
          AND (@Subject IS NULL OR auth.Subject = @Subject)
          AND (@Status IS NULL OR auth.Status = @Status)
          AND (@ClientIdContains IS NULL OR app.ClientId LIKE CONCAT('%', @ClientIdContains, '%'))
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListAuthorizationsFilteredSqlServer = new(
        "identity.list_oidc_authorizations_filtered.sql_server",
        $"""
        SELECT {AuthorizationDetailColumns}
        FROM fn_identity_oidc_authorization AS auth
        LEFT JOIN fn_identity_oidc_application AS app ON app.Id = auth.ApplicationId
        WHERE (@ApplicationId IS NULL OR auth.ApplicationId = @ApplicationId)
          AND (@Subject IS NULL OR auth.Subject = @Subject)
          AND (@Status IS NULL OR auth.Status = @Status)
          AND (@ClientIdContains IS NULL OR app.ClientId LIKE '%' + @ClientIdContains + '%')
        ORDER BY auth.CreatedAtUtc DESC, auth.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListAuthorizationsFilteredMySql = new(
        "identity.list_oidc_authorizations_filtered.mysql",
        $"""
        SELECT {AuthorizationDetailColumns}
        FROM fn_identity_oidc_authorization AS auth
        LEFT JOIN fn_identity_oidc_application AS app ON app.Id = auth.ApplicationId
        WHERE (@ApplicationId IS NULL OR auth.ApplicationId = @ApplicationId)
          AND (@Subject IS NULL OR auth.Subject = @Subject)
          AND (@Status IS NULL OR auth.Status = @Status)
          AND (@ClientIdContains IS NULL OR app.ClientId LIKE CONCAT('%', @ClientIdContains, '%'))
        ORDER BY auth.CreatedAtUtc DESC, auth.Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeAuthorizationById = new(
        "identity.revoke_oidc_authorization_by_id",
        """
        UPDATE fn_identity_oidc_authorization
        SET Status = @RevokedStatus, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE Id = @Id AND Status <> @RevokedStatus
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountAuthorizations = new(
        "identity.count_oidc_authorizations",
        "SELECT COUNT(1) AS Count FROM fn_identity_oidc_authorization",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListAuthorizationsSqlServer = new(
        "identity.list_oidc_authorizations.sql_server",
        $"""
        SELECT {AuthorizationColumns}
        FROM fn_identity_oidc_authorization AS auth
        ORDER BY auth.CreatedAtUtc DESC, auth.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListAuthorizationsMySql = new(
        "identity.list_oidc_authorizations.mysql",
        $"""
        SELECT {AuthorizationColumns}
        FROM fn_identity_oidc_authorization AS auth
        ORDER BY auth.CreatedAtUtc DESC, auth.Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindAuthorizationsByApplicationId = new(
        "identity.find_oidc_authorizations_by_application_id",
        $"SELECT {AuthorizationColumns} FROM fn_identity_oidc_authorization AS auth WHERE auth.ApplicationId = @ApplicationId",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindAuthorizationsBySubject = new(
        "identity.find_oidc_authorizations_by_subject",
        $"SELECT {AuthorizationColumns} FROM fn_identity_oidc_authorization AS auth WHERE auth.Subject = @Subject",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindAuthorizationsByFilter = new(
        "identity.find_oidc_authorizations_by_filter",
        $"""
        SELECT {AuthorizationColumns}
        FROM fn_identity_oidc_authorization AS auth
        WHERE (@Subject IS NULL OR auth.Subject = @Subject)
          AND (@ApplicationId IS NULL OR auth.ApplicationId = @ApplicationId)
          AND (@Status IS NULL OR auth.Status = @Status)
          AND (@Type IS NULL OR auth.Type = @Type)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeAuthorizationsByFilter = new(
        "identity.revoke_oidc_authorizations_by_filter",
        """
        UPDATE fn_identity_oidc_authorization
        SET Status = @RevokedStatus, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE Status <> @RevokedStatus
          AND (@Subject IS NULL OR Subject = @Subject)
          AND (@ApplicationId IS NULL OR ApplicationId = @ApplicationId)
          AND (@StatusFilter IS NULL OR Status = @StatusFilter)
          AND (@Type IS NULL OR Type = @Type)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeAuthorizationsByApplicationId = new(
        "identity.revoke_oidc_authorizations_by_application_id",
        """
        UPDATE fn_identity_oidc_authorization
        SET Status = @RevokedStatus, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE ApplicationId = @ApplicationId AND Status <> @RevokedStatus
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeAuthorizationsBySubject = new(
        "identity.revoke_oidc_authorizations_by_subject",
        """
        UPDATE fn_identity_oidc_authorization
        SET Status = @RevokedStatus, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE Subject = @Subject AND Status <> @RevokedStatus
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PruneAuthorizations = new(
        "identity.prune_oidc_authorizations",
        """
        DELETE FROM fn_identity_oidc_authorization
        WHERE CreationDateUtc < @Threshold
          AND Status <> @ValidStatus
          AND NOT EXISTS (
              SELECT 1 FROM fn_identity_oidc_token AS token
              WHERE token.AuthorizationId = fn_identity_oidc_authorization.Id)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertScope = new(
        "identity.insert_oidc_scope",
        """
        INSERT INTO fn_identity_oidc_scope
            (Id, Name, Description, DescriptionsJson, DisplayName, DisplayNamesJson, PropertiesJson,
             ResourcesJson, Version, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @Name, @Description, @DescriptionsJson, @DisplayName, @DisplayNamesJson, @PropertiesJson,
             @ResourcesJson, @Version, @CreatedAtUtc, @UpdatedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateScope = new(
        "identity.update_oidc_scope",
        """
        UPDATE fn_identity_oidc_scope
        SET Name = @Name, Description = @Description, DescriptionsJson = @DescriptionsJson,
            DisplayName = @DisplayName, DisplayNamesJson = @DisplayNamesJson, PropertiesJson = @PropertiesJson,
            ResourcesJson = @ResourcesJson, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteScope = new(
        "identity.delete_oidc_scope",
        "DELETE FROM fn_identity_oidc_scope WHERE Id = @Id AND Version = @Version",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindScopeById = new(
        "identity.find_oidc_scope_by_id",
        $"SELECT {ScopeColumns} FROM fn_identity_oidc_scope AS scope WHERE scope.Id = @Id",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindScopeByName = new(
        "identity.find_oidc_scope_by_name",
        $"SELECT {ScopeColumns} FROM fn_identity_oidc_scope AS scope WHERE scope.Name = @Name",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindScopesByResourceSqlServer = new(
        "identity.find_oidc_scopes_by_resource.sql_server",
        $"""
        SELECT {ScopeColumns}
        FROM fn_identity_oidc_scope AS scope
        WHERE scope.ResourcesJson IS NOT NULL
          AND EXISTS (SELECT 1 FROM OPENJSON(scope.ResourcesJson) WHERE [value] = @Resource)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindScopesByResourceMySql = new(
        "identity.find_oidc_scopes_by_resource.mysql",
        $"""
        SELECT {ScopeColumns}
        FROM fn_identity_oidc_scope AS scope
        WHERE scope.ResourcesJson IS NOT NULL
          AND JSON_CONTAINS(scope.ResourcesJson, JSON_QUOTE(@Resource))
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountScopes = new(
        "identity.count_oidc_scopes",
        "SELECT COUNT(1) AS Count FROM fn_identity_oidc_scope",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListScopesSqlServer = new(
        "identity.list_oidc_scopes.sql_server",
        $"""
        SELECT {ScopeColumns}
        FROM fn_identity_oidc_scope AS scope
        ORDER BY scope.CreatedAtUtc DESC, scope.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListScopesMySql = new(
        "identity.list_oidc_scopes.mysql",
        $"""
        SELECT {ScopeColumns}
        FROM fn_identity_oidc_scope AS scope
        ORDER BY scope.CreatedAtUtc DESC, scope.Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement InsertToken = new(
        "identity.insert_oidc_token",
        """
        INSERT INTO fn_identity_oidc_token
            (Id, ApplicationId, AuthorizationId, CreationDateUtc, ExpirationDateUtc, Payload, PropertiesJson,
             RedemptionDateUtc, ReferenceId, Status, Subject, Type, Version, CreatedAtUtc, UpdatedAtUtc)
        VALUES
            (@Id, @ApplicationId, @AuthorizationId, @CreationDateUtc, @ExpirationDateUtc, @Payload, @PropertiesJson,
             @RedemptionDateUtc, @ReferenceId, @Status, @Subject, @Type, @Version, @CreatedAtUtc, @UpdatedAtUtc)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement UpdateToken = new(
        "identity.update_oidc_token",
        """
        UPDATE fn_identity_oidc_token
        SET ApplicationId = @ApplicationId, AuthorizationId = @AuthorizationId,
            CreationDateUtc = @CreationDateUtc, ExpirationDateUtc = @ExpirationDateUtc,
            Payload = @Payload, PropertiesJson = @PropertiesJson, RedemptionDateUtc = @RedemptionDateUtc,
            ReferenceId = @ReferenceId, Status = @Status, Subject = @Subject, Type = @Type,
            UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE Id = @Id AND Version = @Version
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RedeemAuthorizationCodeToken = new(
        "identity.redeem_oidc_authorization_code_token",
        """
        UPDATE token
        SET token.RedemptionDateUtc = @RedemptionDateUtc,
            token.Status = @RedeemedStatus,
            token.UpdatedAtUtc = @UpdatedAtUtc,
            token.Version = token.Version + 1
        FROM fn_identity_oidc_token AS token
        INNER JOIN fn_identity_oidc_authorization AS auth ON auth.Id = token.AuthorizationId
        WHERE token.Id = @Id
          AND token.Version = @Version
          AND token.RedemptionDateUtc IS NULL
          AND token.Status = @ValidStatus
          AND auth.Status = @ValidStatus
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RedeemAuthorizationCodeTokenMySql = new(
        "identity.redeem_oidc_authorization_code_token.mysql",
        """
        UPDATE fn_identity_oidc_token AS token
        INNER JOIN fn_identity_oidc_authorization AS auth ON auth.Id = token.AuthorizationId
        SET token.RedemptionDateUtc = @RedemptionDateUtc,
            token.Status = @RedeemedStatus,
            token.UpdatedAtUtc = @UpdatedAtUtc,
            token.Version = token.Version + 1
        WHERE token.Id = @Id
          AND token.Version = @Version
          AND token.RedemptionDateUtc IS NULL
          AND token.Status = @ValidStatus
          AND auth.Status = @ValidStatus
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement DeleteToken = new(
        "identity.delete_oidc_token",
        "DELETE FROM fn_identity_oidc_token WHERE Id = @Id AND Version = @Version",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindTokenById = new(
        "identity.find_oidc_token_by_id",
        $"SELECT {TokenColumns} FROM fn_identity_oidc_token AS token WHERE token.Id = @Id",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindTokenByReferenceId = new(
        "identity.find_oidc_token_by_reference_id",
        $"SELECT {TokenColumns} FROM fn_identity_oidc_token AS token WHERE token.ReferenceId = @ReferenceId",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement CountTokens = new(
        "identity.count_oidc_tokens",
        "SELECT COUNT(1) AS Count FROM fn_identity_oidc_token",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListTokensSqlServer = new(
        "identity.list_oidc_tokens.sql_server",
        $"""
        SELECT {TokenColumns}
        FROM fn_identity_oidc_token AS token
        ORDER BY token.CreatedAtUtc DESC, token.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement ListTokensMySql = new(
        "identity.list_oidc_tokens.mysql",
        $"""
        SELECT {TokenColumns}
        FROM fn_identity_oidc_token AS token
        ORDER BY token.CreatedAtUtc DESC, token.Id DESC
        LIMIT @PageSize OFFSET @Offset
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindTokensByApplicationId = new(
        "identity.find_oidc_tokens_by_application_id",
        $"SELECT {TokenColumns} FROM fn_identity_oidc_token AS token WHERE token.ApplicationId = @ApplicationId",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindTokensByAuthorizationId = new(
        "identity.find_oidc_tokens_by_authorization_id",
        $"SELECT {TokenColumns} FROM fn_identity_oidc_token AS token WHERE token.AuthorizationId = @AuthorizationId",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindTokensBySubject = new(
        "identity.find_oidc_tokens_by_subject",
        $"SELECT {TokenColumns} FROM fn_identity_oidc_token AS token WHERE token.Subject = @Subject",
        SqlDataScope.HostOnly);

    public static readonly SqlStatement FindTokensByFilter = new(
        "identity.find_oidc_tokens_by_filter",
        $"""
        SELECT {TokenColumns}
        FROM fn_identity_oidc_token AS token
        WHERE (@Subject IS NULL OR token.Subject = @Subject)
          AND (@ApplicationId IS NULL OR token.ApplicationId = @ApplicationId)
          AND (@Status IS NULL OR token.Status = @Status)
          AND (@Type IS NULL OR token.Type = @Type)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeTokensByFilter = new(
        "identity.revoke_oidc_tokens_by_filter",
        """
        UPDATE fn_identity_oidc_token
        SET Status = @RevokedStatus, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE Status <> @RevokedStatus
          AND (@Subject IS NULL OR Subject = @Subject)
          AND (@ApplicationId IS NULL OR ApplicationId = @ApplicationId)
          AND (@StatusFilter IS NULL OR Status = @StatusFilter)
          AND (@Type IS NULL OR Type = @Type)
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeTokensByApplicationId = new(
        "identity.revoke_oidc_tokens_by_application_id",
        """
        UPDATE fn_identity_oidc_token
        SET Status = @RevokedStatus, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE ApplicationId = @ApplicationId AND Status <> @RevokedStatus
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeTokensByAuthorizationId = new(
        "identity.revoke_oidc_tokens_by_authorization_id",
        """
        UPDATE fn_identity_oidc_token
        SET Status = @RevokedStatus, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE AuthorizationId = @AuthorizationId AND Status <> @RevokedStatus
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement RevokeTokensBySubject = new(
        "identity.revoke_oidc_tokens_by_subject",
        """
        UPDATE fn_identity_oidc_token
        SET Status = @RevokedStatus, UpdatedAtUtc = @UpdatedAtUtc, Version = Version + 1
        WHERE Subject = @Subject AND Status <> @RevokedStatus
        """,
        SqlDataScope.HostOnly);

    public static readonly SqlStatement PruneTokens = new(
        "identity.prune_oidc_tokens",
        """
        DELETE FROM fn_identity_oidc_token
        WHERE CreationDateUtc < @Threshold
          AND (Status <> @ValidStatus OR ExpirationDateUtc < @Threshold)
        """,
        SqlDataScope.HostOnly);
}