[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string] $ApplicationId,

    [ValidatePattern('^[^/]+/[^/]+$')]
    [string] $Repository = 'MattKotsenas/MattKotsenas',
    [string] $ApplicationName = 'github-MattKotsenas-blog'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-JsonCommand {
    param(
        [Parameter(Mandatory)]
        [string] $Command,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$Command $($Arguments -join ' ')' failed."
    }

    if ([string]::IsNullOrWhiteSpace(($output -join [Environment]::NewLine))) {
        return $null
    }

    return ($output -join [Environment]::NewLine) | ConvertFrom-Json
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string] $Command,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$Command $($Arguments -join ' ')' failed."
    }
}

foreach ($command in 'az', 'gh') {
    if ($null -eq (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command '$command' was not found."
    }
}

$account = Invoke-JsonCommand az @('account', 'show', '--only-show-errors', '--output', 'json')
$repositoryInfo = Invoke-JsonCommand gh @('api', "repos/$Repository")
$Repository = "$($repositoryInfo.owner.login)/$($repositoryInfo.name)"
$immutableSubjectPrefix = "repo:$($repositoryInfo.owner.login)@$($repositoryInfo.owner.id)/$($repositoryInfo.name)@$($repositoryInfo.id)"

$subscriptionId = [string] $account.id
$tenantId = [string] $account.tenantId
$scope = "/subscriptions/$subscriptionId"

if (-not $PSCmdlet.ShouldProcess(
        "$Repository and Azure subscription $subscriptionId",
        'Configure the GitHub Actions federated deployment identity')) {
    return
}

$hasExplicitApplicationId = -not [string]::IsNullOrWhiteSpace($ApplicationId)
$app = if (-not $hasExplicitApplicationId) {
    $existingApps = @(
        @(
            Invoke-JsonCommand az @(
                'ad', 'app', 'list',
                '--display-name', $ApplicationName,
                '--only-show-errors',
                '--output', 'json')
        ) | Where-Object displayName -CEQ $ApplicationName
    )

    if ($existingApps.Count -ne 0) {
        $applicationIds = $existingApps.appId -join ', '
        throw "An Entra application named '$ApplicationName' already exists. Verify its ownership, then rerun with -ApplicationId. Matching application IDs: $applicationIds"
    }

    $createdApp = Invoke-JsonCommand az @(
        'ad', 'app', 'create',
        '--display-name', $ApplicationName,
        '--sign-in-audience', 'AzureADMyOrg',
        '--only-show-errors',
        '--output', 'json')

    $matchingApps = @(
        @(
            Invoke-JsonCommand az @(
                'ad', 'app', 'list',
                '--display-name', $ApplicationName,
                '--only-show-errors',
                '--output', 'json')
        ) | Where-Object displayName -CEQ $ApplicationName
    )

    if ($matchingApps.Count -ne 1 -or
        [string] $matchingApps[0].appId -cne [string] $createdApp.appId) {
        throw "Application-name collision detected for '$ApplicationName'. No roles were assigned."
    }

    $createdApp
} else {
    Invoke-JsonCommand az @(
        'ad', 'app', 'show',
        '--id', $ApplicationId,
        '--only-show-errors',
        '--output', 'json')
}

$servicePrincipals = @(
    Invoke-JsonCommand az @(
        'ad', 'sp', 'list',
        '--filter', "appId eq '$($app.appId)'",
        '--only-show-errors',
        '--output', 'json')
)
$servicePrincipal = if ($servicePrincipals.Count -eq 1) {
    $servicePrincipals[0]
} elseif ($servicePrincipals.Count -eq 0) {
    Invoke-JsonCommand az @(
        'ad', 'sp', 'create',
        '--id', [string] $app.appId,
        '--only-show-errors',
        '--output', 'json')
} else {
    throw "More than one service principal uses application ID '$($app.appId)'."
}

$credentialName = 'github-main-immutable'
$credentials = @(
    Invoke-JsonCommand az @(
        'ad', 'app', 'federated-credential', 'list',
        '--id', [string] $app.id,
        '--only-show-errors',
        '--output', 'json')
)
$unexpectedCredentials = @(
    $credentials | Where-Object name -CNE $credentialName
)
if ($unexpectedCredentials.Count -ne 0) {
    $unexpectedNames = $unexpectedCredentials.name -join ', '
    throw "The deployment application contains unrecognized federated credentials: $unexpectedNames"
}

$matchingCredentials = @(
    $credentials | Where-Object name -CEQ $credentialName
)
if ($matchingCredentials.Count -gt 1) {
    throw "The deployment application contains duplicate '$credentialName' credentials."
}
$credential = if ($matchingCredentials.Count -eq 1) {
    $matchingCredentials[0]
} else {
    $null
}

$credentialParameters = [ordered]@{
    issuer      = 'https://token.actions.githubusercontent.com'
    subject     = "$immutableSubjectPrefix`:ref:refs/heads/main"
    description = 'GitHub Actions deployment from the main branch'
    audiences   = @('api://AzureADTokenExchange')
}

$credentialAudiences = @(
    if ($null -ne $credential) {
        $credential.audiences
    }
)
$credentialNeedsUpdate =
    $null -ne $credential -and (
        $credential.issuer -cne $credentialParameters.issuer -or
        $credential.subject -cne $credentialParameters.subject -or
        $credential.description -cne $credentialParameters.description -or
        $credentialAudiences.Count -ne 1 -or
        [string] $credentialAudiences[0] -cne $credentialParameters.audiences[0])

if ($null -eq $credential -or $credentialNeedsUpdate) {
    $credentialFile = [IO.Path]::GetTempFileName()

    try {
        $parameters = if ($null -eq $credential) {
            [ordered]@{
                name        = $credentialName
                issuer      = $credentialParameters.issuer
                subject     = $credentialParameters.subject
                description = $credentialParameters.description
                audiences   = $credentialParameters.audiences
            }
        } else {
            $credentialParameters
        }

        $parameters |
            ConvertTo-Json |
            Set-Content -LiteralPath $credentialFile -Encoding utf8NoBOM

        if ($null -eq $credential) {
            Invoke-NativeCommand az @(
                'ad', 'app', 'federated-credential', 'create',
                '--id', [string] $app.id,
                '--parameters', $credentialFile,
                '--only-show-errors',
                '--output', 'none')
        } else {
            Invoke-NativeCommand az @(
                'ad', 'app', 'federated-credential', 'update',
                '--id', [string] $app.id,
                '--federated-credential-id', $credentialName,
                '--parameters', $credentialFile,
                '--only-show-errors',
                '--output', 'none')
        }
    } finally {
        Remove-Item -LiteralPath $credentialFile -Force
    }
}

Invoke-NativeCommand gh @(
    'api',
    '--method', 'PUT',
    "repos/$Repository/actions/oidc/customization/sub",
    '-F', 'use_default=true',
    '-F', 'use_immutable_subject=true',
    '--silent')
$oidcConfig = Invoke-JsonCommand gh @(
    'api',
    "repos/$Repository/actions/oidc/customization/sub")
if (
    $oidcConfig.use_default -ne $true -or
    $oidcConfig.use_immutable_subject -ne $true -or
    [string] $oidcConfig.sub_claim_prefix -cne $immutableSubjectPrefix) {
    throw 'GitHub did not enable the expected immutable default OIDC subject.'
}

$roleAssignments = @(
    Invoke-JsonCommand az @(
        'role', 'assignment', 'list',
        '--assignee', [string] $servicePrincipal.id,
        '--scope', $scope,
        '--only-show-errors',
        '--output', 'json')
)

foreach ($role in 'Contributor', 'Role Based Access Control Administrator') {
    if (@($roleAssignments | Where-Object roleDefinitionName -CEQ $role).Count -eq 0) {
        $verifiedApp = Invoke-JsonCommand az @(
            'ad', 'app', 'show',
            '--id', [string] $app.appId,
            '--only-show-errors',
            '--output', 'json')
        if ([string] $verifiedApp.appId -cne [string] $app.appId) {
            throw "The deployment application changed during setup. No role was assigned."
        }

        Invoke-NativeCommand az @(
            'role', 'assignment', 'create',
            '--assignee-object-id', [string] $servicePrincipal.id,
            '--assignee-principal-type', 'ServicePrincipal',
            '--role', $role,
            '--scope', $scope,
            '--only-show-errors',
            '--output', 'none')
    }
}

Invoke-NativeCommand gh @(
    'variable', 'set', 'AZURE_CLIENT_ID',
    '--repo', $Repository,
    '--body', [string] $app.appId)
Invoke-NativeCommand gh @(
    'variable', 'set', 'AZURE_TENANT_ID',
    '--repo', $Repository,
    '--body', $tenantId)
Invoke-NativeCommand gh @(
    'variable', 'set', 'AZURE_SUBSCRIPTION_ID',
    '--repo', $Repository,
    '--body', $subscriptionId)

Write-Output "Configured application '$($app.appId)' for deployments from the main branch."
