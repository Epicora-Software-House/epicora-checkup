<#
.SYNOPSIS
    Sonda de fontes de dados do Epicora Checkup. NÃO produz relatório.

.DESCRIPTION
    Executa cada fonte marcada com confiança M ou B no documento técnico e grava,
    em bruto, o que ela devolveu — incluindo o erro, quando falha.

    Este script é o instrumento que resolve empiricamente as incertezas listadas
    em docs/02-especificacao-tecnica.md §12. Sem ele, a Fase 2 seria construída
    sobre suposições.

    NÃO ALTERA NADA NA MÁQUINA. Somente leitura, com uma exceção declarada:
    powercfg /batteryreport escreve um arquivo HTML na pasta temporária, que é
    lido e apagado em seguida.

.PARAMETER OutputPath
    Pasta onde gravar o JSON da sonda. Padrão: .\EpicoraCheckup-Probes

.PARAMETER SkipBatteryReport
    Pula powercfg /batteryreport, que é a única etapa que escreve arquivo.

.EXAMPLE
    .\Test-DataSources.ps1
    .\Test-DataSources.ps1 -OutputPath D:\sondas -SkipBatteryReport

.NOTES
    Alvo: Windows PowerShell 5.1. Não usa recursos de PowerShell 7.
    Rodar as duas vezes: elevado e não elevado. A diferença entre as duas saídas
    é o que define RequiresElevation de cada coletor na Fase 2.
#>
[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot 'EpicoraCheckup-Probes'),
    [switch] $SkipBatteryReport
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$script:Probes = [ordered]@{}

function Test-Elevated {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

<#
    Executa uma sonda isolada. Nada aqui pode derrubar o script:
    o objetivo é justamente descobrir o que falha e por quê.
#>
function Invoke-Probe {
    param(
        [Parameter(Mandatory)] [string]    $Id,
        [Parameter(Mandatory)] [string]    $Source,
        [Parameter(Mandatory)] [string]    $Confidence,   # M ou B
        [Parameter(Mandatory)] [string]    $Question,     # o que esta sonda decide
        [Parameter(Mandatory)] [scriptblock] $Script
    )

    Write-Host ('  {0,-28} ' -f $Id) -NoNewline
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $entry = [ordered]@{
        source     = $Source
        confidence = $Confidence
        question   = $Question
        ok         = $false
        durationMs = 0
        raw        = $null
        error      = $null
    }

    try {
        $entry.raw = & $Script
        $entry.ok = $true
        Write-Host 'ok' -ForegroundColor Green
    }
    catch {
        $entry.error = [ordered]@{
            message   = $_.Exception.Message
            type      = $_.Exception.GetType().FullName
            category  = $_.CategoryInfo.Category.ToString()
        }
        Write-Host 'falhou' -ForegroundColor Yellow
        Write-Host ('      {0}' -f $_.Exception.Message) -ForegroundColor DarkGray
    }
    finally {
        $sw.Stop()
        $entry.durationMs = [int]$sw.ElapsedMilliseconds
        $script:Probes[$Id] = $entry
    }
}

# Reduz um objeto WMI/CIM a um hashtable simples com as propriedades pedidas.
# Sem isso, ConvertTo-Json engasga com as propriedades de sistema do CIM.
function Select-Raw {
    param($InputObject, [string[]] $Property)
    if ($null -eq $InputObject) { return $null }
    $out = @()
    foreach ($item in @($InputObject)) {
        $h = [ordered]@{}
        foreach ($p in $Property) {
            $h[$p] = if ($item.PSObject.Properties.Name -contains $p) { $item.$p } else { '<<propriedade ausente>>' }
        }
        $out += [pscustomobject]$h
    }
    return $out
}

# ============================================================================

$elevated = Test-Elevated
Write-Host ''
Write-Host 'Epicora Checkup — sonda de fontes de dados' -ForegroundColor Cyan
Write-Host ("Elevado: {0}" -f $(if ($elevated) { 'SIM' } else { 'NÃO' })) -ForegroundColor Cyan
Write-Host 'Somente leitura. Nada é alterado nesta máquina.' -ForegroundColor DarkGray
Write-Host ''

# ---------------------------------------------------------------- identificação

Invoke-Probe -Id 'chassisTypes' -Confidence 'M' `
    -Source 'Win32_SystemEnclosure.ChassisTypes' `
    -Question 'A tabela codigo->tipo bate? O fabricante preencheu certo? Bateria confirma notebook?' {
    [ordered]@{
        enclosure = Select-Raw (Get-CimInstance Win32_SystemEnclosure) @('ChassisTypes','Manufacturer','SMBIOSAssetTag')
        batteryPresent = @(Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue).Count -gt 0
        pcSystemType = (Get-CimInstance Win32_ComputerSystem).PCSystemType
    }
}

# ---------------------------------------------------------------- memória

Invoke-Probe -Id 'memoryType' -Confidence 'M' `
    -Source 'Win32_PhysicalMemory.SMBIOSMemoryType vs MemoryType' `
    -Question 'Qual das duas propriedades devolve o tipo correto? Qual a tabela de codigos real?' {
    Select-Raw (Get-CimInstance Win32_PhysicalMemory) `
        @('BankLabel','DeviceLocator','Capacity','Speed','ConfiguredClockSpeed','MemoryType','SMBIOSMemoryType','Manufacturer','PartNumber')
}

Invoke-Probe -Id 'memorySlots' -Confidence 'M' `
    -Source 'Win32_PhysicalMemoryArray' `
    -Question 'MemoryDevices devolve o total de slots fisicos? MaxCapacity e confiavel ou vem zero/absurdo?' {
    [ordered]@{
        array = Select-Raw (Get-CimInstance Win32_PhysicalMemoryArray) `
            @('MemoryDevices','MaxCapacity','MaxCapacityEx','Tag','Use')
        modulesInstalled = @(Get-CimInstance Win32_PhysicalMemory).Count
    }
}

# ---------------------------------------------------------------- armazenamento

Invoke-Probe -Id 'physicalDiskMediaType' -Confidence 'M' `
    -Source 'MSFT_PhysicalDisk em root\Microsoft\Windows\Storage' `
    -Question 'MediaType e BusType devolvem valores utilizaveis? Precisa de elevacao? Qual o mapa de codigos?' {
    [ordered]@{
        msftPhysicalDisk = Select-Raw (Get-CimInstance -Namespace 'root\Microsoft\Windows\Storage' -ClassName MSFT_PhysicalDisk) `
            @('DeviceId','FriendlyName','MediaType','BusType','HealthStatus','Size','SerialNumber','SpindleSpeed')
        # Comparativo deliberado: Win32_DiskDrive.MediaType e a ARMADILHA — devolve
        # 'Fixed hard disk media' tambem para SSD. Registrado para provar a diferenca.
        win32DiskDriveArmadilha = Select-Raw (Get-CimInstance Win32_DiskDrive) `
            @('Index','Model','MediaType','InterfaceType','Size','SerialNumber')
    }
}

Invoke-Probe -Id 'smartFailurePredict' -Confidence 'M' `
    -Source 'MSStorageDriver_FailurePredictStatus em root\wmi' `
    -Question 'Responde nesta maquina? Exige elevacao? Funciona em NVMe e em RAID?' {
    Select-Raw (Get-CimInstance -Namespace 'root\wmi' -ClassName MSStorageDriver_FailurePredictStatus) `
        @('InstanceName','PredictFailure','Reason','Active')
}

Invoke-Probe -Id 'trimAndPartitionStyle' -Confidence 'B' `
    -Source 'fsutil behavior query DisableDeleteNotify + Get-Disk' `
    -Question 'Da para saber o estado do TRIM de forma confiavel? Qual o estilo de particao do disco de sistema?' {
    [ordered]@{
        disableDeleteNotify = (& fsutil.exe behavior query DisableDeleteNotify 2>&1 | Out-String).Trim()
        disks = Select-Raw (Get-CimInstance -Namespace 'root\Microsoft\Windows\Storage' -ClassName MSFT_Disk) `
            @('Number','FriendlyName','PartitionStyle','IsSystem','IsBoot','Size')
    }
}

# ---------------------------------------------------------------- licenciamento

Invoke-Probe -Id 'activation' -Confidence 'M' `
    -Source 'SoftwareLicensingProduct' `
    -Question 'Quanto tempo leva? Quais valores de LicenseStatus aparecem? Exige elevacao?' {
    # Mesma consulta que o coletor usa, para que a sonda valide o que sera usado de verdade.
    # NOTA DE AUDITORIA: PartialProductKey aparece so na clausula WHERE, como FILTRO — e o
    # que distingue a licenca do Windows instalado das outras linhas da classe. A lista do
    # SELECT nao inclui nenhum fragmento de chave. Nada de chave e lido ou gravado.
    Select-Raw (Get-CimInstance -Query "SELECT Name, Description, LicenseStatus, LicenseStatusReason, GracePeriodRemaining, ProductKeyChannel FROM SoftwareLicensingProduct WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL") `
        @('Name','Description','LicenseStatus','LicenseStatusReason','GracePeriodRemaining','ProductKeyChannel')
}

# ---------------------------------------------------------------- Windows 11

Invoke-Probe -Id 'tpm' -Confidence 'M' `
    -Source 'Win32_Tpm em root\CIMV2\Security\MicrosoftTpm' `
    -Question 'Qual o formato exato de SpecVersion? Como distinguir ausente de inacessivel?' {
    Select-Raw (Get-CimInstance -Namespace 'root\CIMV2\Security\MicrosoftTpm' -ClassName Win32_Tpm) `
        @('SpecVersion','ManufacturerVersion','ManufacturerIdTxt','IsEnabled_InitialValue','IsActivated_InitialValue','IsOwned_InitialValue','PhysicalPresenceVersionInfo')
}

Invoke-Probe -Id 'secureBoot' -Confidence 'M' `
    -Source 'Registro SecureBoot\State + Confirm-SecureBootUEFI' `
    -Question 'O registro responde sem elevacao? O cmdlet lanca excecao em BIOS legado, como previsto?' {
    $reg = $null; $regErr = $null
    try {
        $reg = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State' -Name UEFISecureBootEnabled -ErrorAction Stop).UEFISecureBootEnabled
    } catch { $regErr = $_.Exception.Message }

    $cmdlet = $null; $cmdletErr = $null
    try { $cmdlet = Confirm-SecureBootUEFI -ErrorAction Stop } catch { $cmdletErr = $_.Exception.Message }

    [ordered]@{
        registryValue = $reg; registryError = $regErr
        cmdletValue = $cmdlet; cmdletError = $cmdletErr
    }
}

Invoke-Probe -Id 'firmwareMode' -Confidence 'B' `
    -Source 'variavel firmware_type vs estilo de particao' `
    -Question 'QUAL DOS TRES CAMINHOS USAR para distinguir UEFI de BIOS legado? Eles concordam?' {
    $sysDrive = $env:SystemDrive
    $sysDiskNumber = $null
    try {
        $part = Get-CimInstance -Namespace 'root\Microsoft\Windows\Storage' -ClassName MSFT_Partition |
                Where-Object { $_.DriveLetter -eq $sysDrive.TrimEnd(':') }
        if ($part) { $sysDiskNumber = $part.DiskNumber }
    } catch { }

    [ordered]@{
        envFirmwareType = $env:firmware_type
        setupactSuggests = $null
        systemDiskNumber = $sysDiskNumber
        systemDiskPartitionStyle = if ($null -ne $sysDiskNumber) {
            (Get-CimInstance -Namespace 'root\Microsoft\Windows\Storage' -ClassName MSFT_Disk |
             Where-Object { $_.Number -eq $sysDiskNumber }).PartitionStyle
        } else { $null }
        efiSystemPartitionPresent = [bool](Get-CimInstance -Namespace 'root\Microsoft\Windows\Storage' -ClassName MSFT_Partition -ErrorAction SilentlyContinue |
                                           Where-Object { $_.GptType -eq '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}' })
    }
}

# ---------------------------------------------------------------- segurança

Invoke-Probe -Id 'antivirusProductState' -Confidence 'B' `
    -Source 'AntiVirusProduct em root\SecurityCenter2' `
    -Question 'A MAIS IMPORTANTE. productState em bruto e hex, para decodificar depois com dados reais de varias maquinas.' {
    # NAO INTERPRETAR AQUI. O valor bruto e o dado. A interpretacao e mascara de bits
    # NAO documentada pela Microsoft — toda decodificacao que circula e engenharia reversa.
    $prods = Get-CimInstance -Namespace 'root\SecurityCenter2' -ClassName AntiVirusProduct -ErrorAction Stop
    $out = @()
    foreach ($p in @($prods)) {
        $out += [pscustomobject][ordered]@{
            displayName      = $p.displayName
            productState     = $p.productState
            productStateHex  = ('0x{0:X}' -f [int]$p.productState)
            productStateBits = [Convert]::ToString([int]$p.productState, 2).PadLeft(24, '0')
            timestamp        = $p.timestamp
            instanceGuid     = $p.instanceGuid
        }
    }
    [ordered]@{ count = $out.Count; products = $out }
}

Invoke-Probe -Id 'defenderStatus' -Confidence 'M' `
    -Source 'MSFT_MpComputerStatus em root\Microsoft\Windows\Defender' `
    -Question 'Responde mesmo com antivirus de terceiro instalado? Precisa de elevacao?' {
    Select-Raw (Get-CimInstance -Namespace 'root\Microsoft\Windows\Defender' -ClassName MSFT_MpComputerStatus) `
        @('AMServiceEnabled','RealTimeProtectionEnabled','AntivirusSignatureAge','AntivirusSignatureLastUpdated','IsTamperProtected','AMRunningMode','AntispywareEnabled')
}

Invoke-Probe -Id 'bitlocker' -Confidence 'M' `
    -Source 'Win32_EncryptableVolume em root\CIMV2\Security\MicrosoftVolumeEncryption' `
    -Question 'Existe em edicao Home? Quais valores de ProtectionStatus e ConversionStatus aparecem?' {
    Select-Raw (Get-CimInstance -Namespace 'root\CIMV2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume) `
        @('DriveLetter','ProtectionStatus','ConversionStatus','EncryptionMethod','VolumeType')
}

Invoke-Probe -Id 'firewall' -Confidence 'M' `
    -Source 'MSFT_NetFirewallProfile em root\StandardCimv2' `
    -Question 'Nomes de propriedade corretos? Enabled vem booleano ou enum?' {
    Select-Raw (Get-CimInstance -Namespace 'root\StandardCimv2' -ClassName MSFT_NetFirewallProfile) `
        @('Name','Enabled','DefaultInboundAction','DefaultOutboundAction')
}

Invoke-Probe -Id 'smb1' -Confidence 'M' `
    -Source 'Feature opcional SMB1Protocol + registro do servidor SMB' `
    -Question 'Qual caminho responde sem depender do modulo DISM? Concordam entre si?' {
    $feature = $null; $featureErr = $null
    try {
        $feature = Get-CimInstance -Namespace 'root\cimv2' -ClassName Win32_OptionalFeature -Filter "Name='SMB1Protocol'" |
                   Select-Object Name, InstallState
    } catch { $featureErr = $_.Exception.Message }

    $srv = $null
    try {
        $srv = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters' -Name SMB1 -ErrorAction Stop).SMB1
    } catch { $srv = '<<valor ausente — ausencia normalmente significa habilitado>>' }

    [ordered]@{
        optionalFeature = $feature; optionalFeatureError = $featureErr
        lanmanServerSMB1 = $srv
        mrxsmb10Start = try { (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\mrxsmb10' -Name Start -ErrorAction Stop).Start } catch { $null }
    }
}

Invoke-Probe -Id 'localAdministratorsBySid' -Confidence 'M' `
    -Source 'Grupo de administradores locais resolvido por SID' `
    -Question 'CRITICO: resolver por SID (nao por nome, que e localizado). Qual caminho funciona em dominio?' {
    # SID conhecido do grupo Administradores locais.
    $adminSid = 'S-1-5-32-544'
    $groupName = $null; $members = @(); $err = $null
    try {
        $sid = New-Object Security.Principal.SecurityIdentifier($adminSid)
        $groupName = $sid.Translate([Security.Principal.NTAccount]).Value
        $g = Get-CimInstance Win32_Group -Filter "SID='$adminSid'"
        if ($g) {
            $q = "ASSOCIATORS OF {Win32_Group.Domain='$($g.Domain)',Name='$($g.Name)'} WHERE ResultClass=Win32_Account"
            $members = Select-Raw (Get-CimInstance -Query $q) @('Name','Domain','SID','SIDType','Disabled')
        }
    } catch { $err = $_.Exception.Message }

    $me = [Security.Principal.WindowsIdentity]::GetCurrent()
    [ordered]@{
        wellKnownSid       = $adminSid
        resolvedGroupName  = $groupName          # localizado — registrado só para conferência
        members            = $members
        error              = $err
        currentUserName    = $me.Name
        currentUserSid     = $me.User.Value
        currentUserGroupSids = @($me.Groups | ForEach-Object { $_.Value })
    }
}

# ---------------------------------------------------------------- inicialização

Invoke-Probe -Id 'startupApproved' -Confidence 'B' `
    -Source 'Explorer\StartupApproved\Run' `
    -Question 'SOMENTE LEITURA. Como e o formato binario? Da para inferir habilitado/desabilitado pelo primeiro byte?' {
    # LEITURA APENAS. ADR-007 proibe escrever nesta chave: formato binario nao documentado.
    $paths = @(
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder'
    )
    $out = [ordered]@{}
    foreach ($p in $paths) {
        if (-not (Test-Path $p)) { $out[$p] = '<<chave inexistente>>'; continue }
        $item = Get-ItemProperty -Path $p
        $vals = [ordered]@{}
        foreach ($prop in $item.PSObject.Properties) {
            if ($prop.Name -like 'PS*') { continue }
            $vals[$prop.Name] = if ($prop.Value -is [byte[]]) {
                ($prop.Value | ForEach-Object { '{0:X2}' -f $_ }) -join ' '
            } else { $prop.Value }
        }
        $out[$p] = $vals
    }
    $out
}

Invoke-Probe -Id 'startupCoverage' -Confidence 'M' `
    -Source 'Win32_StartupCommand vs chaves Run vs tarefas de logon' `
    -Question 'Quanto Win32_StartupCommand DEIXA DE FORA? A diferenca justifica complementar na Fase 5?' {
    $wmi = Select-Raw (Get-CimInstance Win32_StartupCommand) @('Name','Command','Location','User')
    $runKeys = [ordered]@{}
    foreach ($p in @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce')) {
        if (-not (Test-Path $p)) { $runKeys[$p] = '<<chave inexistente>>'; continue }
        $h = [ordered]@{}
        foreach ($prop in (Get-ItemProperty -Path $p).PSObject.Properties) {
            if ($prop.Name -notlike 'PS*') { $h[$prop.Name] = $prop.Value }
        }
        $runKeys[$p] = $h
    }
    $logonTasks = $null
    try {
        $logonTasks = @(Get-ScheduledTask -ErrorAction Stop |
            Where-Object { $_.State -ne 'Disabled' -and ($_.Triggers | Where-Object { $_.CimClass.CimClassName -eq 'MSFT_TaskLogonTrigger' }) } |
            ForEach-Object { [pscustomobject]@{ TaskName = $_.TaskName; TaskPath = $_.TaskPath; Author = $_.Author } })
    } catch { $logonTasks = "<<Get-ScheduledTask falhou: $($_.Exception.Message)>>" }

    [ordered]@{
        win32StartupCommandCount = @($wmi).Count
        win32StartupCommand      = $wmi
        runKeys                  = $runKeys
        logonTriggeredTaskCount  = if ($logonTasks -is [array]) { $logonTasks.Count } else { $null }
        logonTriggeredTasks      = $logonTasks
    }
}

# ---------------------------------------------------------------- rede

Invoke-Probe -Id 'netAdapterType' -Confidence 'M' `
    -Source 'MSFT_NetAdapter em root\StandardCimv2' `
    -Question 'Como distinguir cabo de Wi-Fi de virtual? Qual propriedade da a velocidade negociada vs a maxima?' {
    [ordered]@{
        msftNetAdapter = Select-Raw (Get-CimInstance -Namespace 'root\StandardCimv2' -ClassName MSFT_NetAdapter) `
            @('Name','InterfaceDescription','MediaType','PhysicalMediaType','Speed','LinkSpeed','Virtual','ConnectorPresent','Status','MacAddress','NdisPhysicalMedium')
        win32NetworkAdapter = Select-Raw (Get-CimInstance Win32_NetworkAdapter -Filter 'PhysicalAdapter=TRUE') `
            @('Name','NetConnectionID','MACAddress','Speed','NetConnectionStatus','AdapterTypeId')
    }
}

# ---------------------------------------------------------------- bateria

if (-not $SkipBatteryReport) {
    Invoke-Probe -Id 'batteryReport' -Confidence 'M' `
        -Source 'powercfg /batteryreport + Win32_Battery' `
        -Question 'DesignCapacity vem nulo no WMI, como previsto? Qual o formato da saida do powercfg? Existe opcao XML?' {
        $wmi = Select-Raw (Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue) `
            @('Name','DeviceID','DesignCapacity','FullChargeCapacity','EstimatedChargeRemaining','BatteryStatus','Chemistry')

        $report = $null
        if (@($wmi).Count -gt 0) {
            # UNICA ESCRITA DE TODO O SCRIPT, e em pasta temporaria. Removida logo abaixo.
            $tmp = Join-Path $env:TEMP ("epicora-battery-{0}.html" -f [guid]::NewGuid().ToString('N'))
            try {
                $null = & powercfg.exe /batteryreport /output $tmp 2>&1
                if (Test-Path $tmp) {
                    $html = Get-Content -Path $tmp -Raw
                    $report = [ordered]@{
                        lengthChars  = $html.Length
                        # Trecho suficiente para descobrir o formato sem inchar o JSON da sonda.
                        designCapacityFragment = ([regex]::Match($html, '(?s)DESIGN CAPACITY.{0,400}')).Value
                        fullChargeFragment     = ([regex]::Match($html, '(?s)FULL CHARGE CAPACITY.{0,400}')).Value
                        cycleCountFragment     = ([regex]::Match($html, '(?s)CYCLE COUNT.{0,300}')).Value
                    }
                }
            } finally {
                if (Test-Path $tmp) { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
            }
        }
        [ordered]@{ win32Battery = $wmi; batteryReport = $report }
    }
}

# ---------------------------------------------------------------- eventos

Invoke-Probe -Id 'eventIdCandidates' -Confidence 'B' `
    -Source 'Event Log, canais System e Application' `
    -Question 'CONFIRMAR OS IDs antes de preencher rules/event-ids.json. Os candidatos abaixo BATEM com a realidade desta maquina?' {
    # Estes IDs sao CANDIDATOS a verificar, nao verdade estabelecida. O documento tecnico
    # §4.11 e explicito: nao registrar ID que nao se pode confirmar. Esta sonda existe
    # para conferir cada um contra uma maquina cujo historico se conhece.
    # NAO coletar canal Security nem eventos de logon (doc 01 §7.1).
    $since = (Get-Date).AddDays(-30)
    $candidates = @(
        @{ cat = 'unexpectedShutdown';       channel = 'System';      id = 41;   provider = 'Microsoft-Windows-Kernel-Power' },
        @{ cat = 'unexpectedShutdown';       channel = 'System';      id = 6008; provider = 'EventLog' },
        @{ cat = 'unexpectedShutdown';       channel = 'System';      id = 1001; provider = 'Microsoft-Windows-WER-SystemErrorReporting' },
        @{ cat = 'diskError';                channel = 'System';      id = 7;    provider = 'disk' },
        @{ cat = 'diskError';                channel = 'System';      id = 51;   provider = 'disk' },
        @{ cat = 'diskError';                channel = 'System';      id = 153;  provider = 'disk' },
        @{ cat = 'diskError';                channel = 'System';      id = 55;   provider = 'Ntfs' },
        @{ cat = 'criticalApplicationError'; channel = 'Application'; id = 1000; provider = 'Application Error' },
        @{ cat = 'criticalApplicationError'; channel = 'Application'; id = 1002; provider = 'Application Hang' }
    )
    $results = @()
    foreach ($c in $candidates) {
        $count = 0; $last = $null; $err = $null
        try {
            $ev = @(Get-WinEvent -FilterHashtable @{ LogName = $c.channel; Id = $c.id; StartTime = $since } -ErrorAction Stop)
            $count = $ev.Count
            if ($count -gt 0) { $last = $ev[0].TimeCreated.ToString('o') }
        } catch {
            # "No events were found" e resultado valido, nao erro.
            if ($_.Exception.Message -notmatch 'No events were found|Nenhum evento') { $err = $_.Exception.Message }
        }
        $results += [pscustomobject][ordered]@{
            category = $c.cat; channel = $c.channel; eventId = $c.id
            expectedProvider = $c.provider; count = $count; lastOccurrence = $last; error = $err
        }
    }
    [ordered]@{ windowDays = 30; since = $since.ToString('o'); candidates = $results }
}

# ---------------------------------------------------------------- gravação

$machine = Get-CimInstance Win32_ComputerSystem
$bios    = Get-CimInstance Win32_BIOS
$os      = Get-CimInstance Win32_OperatingSystem

$document = [ordered]@{
    probeVersion = '1.0'
    capturedAt   = (Get-Date).ToString('o')
    elevated     = $elevated
    host = [ordered]@{
        hostname       = $env:COMPUTERNAME
        manufacturer   = $machine.Manufacturer
        model          = $machine.Model
        biosVersion    = ($bios.SMBIOSBIOSVersion)
        osCaption      = $os.Caption
        osBuild        = $os.BuildNumber
        osArchitecture = $os.OSArchitecture
        osLocale       = (Get-Culture).Name
        psVersion      = $PSVersionTable.PSVersion.ToString()
    }
    probes = $script:Probes
}

if (-not (Test-Path $OutputPath)) { $null = New-Item -ItemType Directory -Path $OutputPath -Force }
$suffix = if ($elevated) { 'elevado' } else { 'sem-elevacao' }
$file = Join-Path $OutputPath ("sonda_{0}_{1}_{2}.json" -f $env:COMPUTERNAME, (Get-Date -Format 'yyyyMMdd-HHmmss'), $suffix)

# UTF-8 SEM BOM: Set-Content -Encoding UTF8 no 5.1 escreve COM BOM, e BOM quebra JSON.parse.
# -Depth 10 e OBRIGATORIO: o padrao do PowerShell 5.1 e 2 e trunca em silencio.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($file, ($document | ConvertTo-Json -Depth 10), $utf8NoBom)

$okCount = @($script:Probes.Values | Where-Object { $_.ok }).Count
$failCount = @($script:Probes.Values).Count - $okCount

Write-Host ''
Write-Host ("{0} sondas ok, {1} falharam" -f $okCount, $failCount) -ForegroundColor Cyan
Write-Host ("Gravado em: {0}" -f $file) -ForegroundColor Cyan
if (-not $elevated) {
    Write-Host 'Rode TAMBEM como Administrador — a diferenca entre as duas saidas' -ForegroundColor Yellow
    Write-Host 'e o que define RequiresElevation de cada coletor na Fase 2.' -ForegroundColor Yellow
}
Write-Host ''
