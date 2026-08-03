<#
.SYNOPSIS
    Epicora Checkup — coletor portátil de inventário e diagnóstico. Protótipo da Fase 1
    e fallback permanente para quando o EDR do cliente bloquear o executável (ADR-009).

.DESCRIPTION
    Coleta inventário de hardware, sistema, software, rede e configuração de segurança,
    e grava um JSON no schema 1.0 — o mesmo que o executável C# produz.

    SOMENTE LEITURA. Não altera nada nesta máquina, não instala nada, não cria serviço,
    não cria tarefa agendada, não abre porta de rede, não envia dado para servidor nenhum.

    Lê apenas metadados. Nunca conteúdo de arquivo, e-mail, mensagem, histórico de
    navegação, senha ou chave de produto.

.PARAMETER Technician
    Nome do técnico responsável. Obrigatório.

.PARAMETER Client
    Nome da empresa cliente. Obrigatório.

.PARAMETER DiagnosticId
    Identificador do diagnóstico, ex. DIAG-2026-014. Obrigatório.

.PARAMETER MachineLabel
    Identificação da máquina no padrão do cliente, ex. ADM-04.

.PARAMETER Responsible
    Usuário principal da máquina.

.PARAMETER Department
    Setor.

.PARAMETER OutputPath
    Pasta de saída. Padrão: .\EpicoraCheckup\<CLIENTE>

.PARAMETER TimeoutSeconds
    Tempo limite por coletor. Padrão 20 s.

.EXAMPLE
    .\Invoke-EpicoraCheckup.ps1 -Technician "Gabriel" -Client "Cliente X" -DiagnosticId "DIAG-2026-014" `
                                -MachineLabel "ADM-04" -Responsible "Maria" -Department "Administrativo"

.NOTES
    Alvo: Windows PowerShell 5.1, presente em toda instalação de Windows desde o 10.
    Não usa recursos de PowerShell 7 — exigir instalação anularia a razão de existir do fallback.

    Roda COM e SEM elevação. Medido em campo: apenas TPM (Win32_Tpm), BitLocker
    (Win32_EncryptableVolume) e SMART (MSStorageDriver_FailurePredictStatus) exigem
    privilégio. Nenhum coletor é ignorado por falta de elevação — cada uma dessas três
    fontes degrada para null isoladamente, e as regras que dependem delas resolvem
    Indeterminate. O relatório sai parcial e honesto, não truncado.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Technician,
    [Parameter(Mandatory)] [string] $Client,
    [Parameter(Mandatory)] [string] $DiagnosticId,
    [string] $Unit,
    [string] $MachineLabel,
    [string] $Responsible,
    [string] $Department,
    [string] $PhysicalLocation,
    [string] $AssetTag,
    [string] $PhysicalCondition,
    [string] $Notes,
    [switch] $CorporateEnvironment,
    [string] $OutputPath,
    [int]    $TimeoutSeconds = 20
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:ToolVersion  = '0.1.0'
$script:SchemaVer    = '1.0'
$script:StartedAt    = Get-Date
$script:Results      = @()
$script:LogLines     = @()

# ============================================================ infraestrutura

function Write-Log {
    param([string] $Level, [string] $Message)
    $line = '{0} [{1,-5}] {2}' -f (Get-Date).ToString('o'), $Level, $Message
    $script:LogLines += $line
    if ($Level -eq 'ERROR') { Write-Verbose $line }
}

function Test-Elevated {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$id).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Prelúdio injetado em cada runspace: os coletores rodam isolados e não enxergam
# as funções deste escopo.
$script:Prelude = @'
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
function Prop { param($Obj, [string]$Name)
    if ($null -eq $Obj) { return $null }
    if ($Obj.PSObject.Properties.Name -notcontains $Name) { return $null }
    $v = $Obj.$Name
    if ($v -is [string] -and $v.Trim() -eq '') { return $null }
    return $v
}
function AsArray { param($x) if ($null -eq $x) { return @() } return @($x) }
# Valor de array para o JSON. A virgula na frente do return e OBRIGATORIA: sem ela o
# array volta pela pipeline e e desenrolado, e uma colecao de UM item vira objeto
# solto — o JSON sai fora do schema, que exige array. Nunca escreva
# "chave = if ($x.Count) { $x } else { $null }" no lugar disto.
function ArrOrNull { param($x)
    if ($null -eq $x) { return $null }
    $a = @($x)
    if ($a.Count -eq 0) { return $null }
    return ,$a
}
function ArrOrEmpty { param($x)
    if ($null -eq $x) { return ,@() }
    return ,@($x)
}
'@

<#
    Executa um coletor com tempo limite, em runspace separado.

    RESSALVA CONHECIDA (doc 02 §3.2): cancelar uma chamada WMI síncrona em andamento
    não é trivial. Se o tempo limite estourar, o runspace é abandonado e a thread pode
    ficar órfã até o processo terminar. É aceitável aqui — o requisito é que a
    ferramenta não fique pendurada na frente do cliente, não que o recurso seja liberado.
    A mesma limitação vale para o orquestrador em C#.
#>
function Invoke-CollectorScript {
    param([scriptblock] $Script, [int] $TimeoutSeconds)

    $ps = [powershell]::Create()
    try {
        $null = $ps.AddScript($script:Prelude + "`n" + $Script.ToString())
        $handle = $ps.BeginInvoke()
        if ($handle.AsyncWaitHandle.WaitOne($TimeoutSeconds * 1000)) {
            $out = $ps.EndInvoke($handle)
            if ($ps.HadErrors -and $ps.Streams.Error.Count -gt 0) {
                throw $ps.Streams.Error[0].Exception
            }
            return @{ TimedOut = $false; Value = @($out)[0] }
        }
        try { $ps.Stop() } catch { }
        return @{ TimedOut = $true; Value = $null }
    }
    finally {
        try { $ps.Dispose() } catch { }
    }
}

<#
    Roda um coletor e devolve um CollectorResult do schema.

    REGRA DURA: nenhuma falha de coletor interrompe a coleta. A ferramenta sempre
    chega ao fim e sempre produz relatório, mesmo parcial.
#>
function Invoke-Collector {
    param(
        [Parameter(Mandatory)] [string] $Id,
        [Parameter(Mandatory)] [string] $DisplayName,
        [Parameter(Mandatory)] [bool]   $RequiresElevation,
        [Parameter(Mandatory)] [scriptblock] $Script,
        [scriptblock] $Summary,
        [scriptblock] $NotApplicable
    )

    Write-Host ('  {0,-38} ' -f $DisplayName) -NoNewline
    Write-Log 'INFO' "coletor $Id iniciado"
    $sw = [Diagnostics.Stopwatch]::StartNew()

    $result = [ordered]@{
        id                = $Id
        displayName       = $DisplayName
        status            = 'Failed'
        skipReason        = $null
        requiresElevation = $RequiresElevation
        durationMs        = 0
        timedOut          = $false
        summary           = $null
        errors            = @()
        data              = $null
    }

    try {
        if ($RequiresElevation -and -not $script:Elevated) {
            $result.status = 'Skipped'
            $result.skipReason = 'sem privilégio administrativo'
            $result.summary = 'Ignorado — sem privilégio'
            Write-Host 'ignorado (sem privilégio)' -ForegroundColor DarkYellow
        }
        elseif ($NotApplicable -and (& $NotApplicable)) {
            $result.status = 'Skipped'
            $result.skipReason = 'não aplicável a esta máquina'
            $result.summary = 'Ignorado — não aplicável'
            Write-Host 'ignorado (não aplicável)' -ForegroundColor DarkGray
        }
        else {
            $run = Invoke-CollectorScript -Script $Script -TimeoutSeconds $TimeoutSeconds
            if ($run.TimedOut) {
                $result.status = 'Failed'
                $result.timedOut = $true
                $result.summary = 'Falhou — tempo limite excedido'
                $result.errors += [ordered]@{
                    source = $Id
                    message = "Coletor excedeu o tempo limite de $TimeoutSeconds s"
                    fatal = $true
                }
                Write-Host 'tempo limite' -ForegroundColor Red
                Write-Log 'ERROR' "coletor $Id excedeu o tempo limite"
            }
            else {
                $result.data = $run.Value
                $result.status = 'Completed'
                if ($Summary) { $result.summary = & $Summary $run.Value }
                Write-Host 'ok' -ForegroundColor Green
            }
        }
    }
    catch {
        $result.status = 'Failed'
        $result.summary = 'Falhou'
        $result.errors += [ordered]@{
            source  = $Id
            message = $_.Exception.Message
            fatal   = $true
        }
        Write-Host 'falhou' -ForegroundColor Red
        Write-Host ('      {0}' -f $_.Exception.Message) -ForegroundColor DarkGray
        Write-Log 'ERROR' "coletor $Id falhou: $($_.Exception.Message)`n$($_.ScriptStackTrace)"
    }
    finally {
        $sw.Stop()
        $result.durationMs = [int]$sw.ElapsedMilliseconds
        Write-Log 'INFO' "coletor $Id terminou em $($result.durationMs) ms com status $($result.status)"
        $script:Results += [pscustomobject]$result
    }
}

function Get-CollectorData {
    param([string] $Id)
    $c = $script:Results | Where-Object { $_.id -eq $Id }
    if ($c -and $c.status -eq 'Completed') { return $c.data }
    return $null
}

function Format-Bytes {
    param($Bytes)
    if ($null -eq $Bytes) { return 'desconhecido' }
    $gb = [math]::Round($Bytes / 1GB, 1)
    return "$gb GB"
}

# ============================================================ execução

$script:Elevated = Test-Elevated

Write-Host ''
Write-Host 'Epicora Checkup' -ForegroundColor Cyan
Write-Host ("versão {0} · protótipo PowerShell · schema {1}" -f $script:ToolVersion, $script:SchemaVer) -ForegroundColor DarkGray
Write-Host ''
Write-Host 'Esta ferramenta lê apenas metadados de hardware, software e configuração.' -ForegroundColor DarkGray
Write-Host 'Não acessa conteúdo de arquivos, e-mails, mensagens ou histórico de navegação.' -ForegroundColor DarkGray
Write-Host ''
Write-Host ("Elevado: {0}" -f $(if ($script:Elevated) { 'SIM' } else { 'NÃO — TPM, BitLocker e SMART não serão lidos' })) `
    -ForegroundColor $(if ($script:Elevated) { 'Cyan' } else { 'Yellow' })
Write-Host ''

# ---------------------------------------------------------------- 1. máquina

Invoke-Collector -Id 'machine' -DisplayName 'Identificação da máquina' -RequiresElevation $false -Script {
    $cs   = Get-CimInstance Win32_ComputerSystem
    $csp  = Get-CimInstance Win32_ComputerSystemProduct
    $bios = Get-CimInstance Win32_BIOS
    $bb   = Get-CimInstance Win32_BaseBoard -ErrorAction SilentlyContinue

    $chassis = @()
    $chassisName = $null
    try {
        $enc = Get-CimInstance Win32_SystemEnclosure
        $chassis = @((Prop $enc 'ChassisTypes') | Where-Object { $null -ne $_ } | ForEach-Object { [int]$_ })
        # Mapa parcial e deliberado. Códigos fora dele viram null, não um palpite.
        $map = @{ 3='Desktop'; 4='Low Profile Desktop'; 5='Pizza Box'; 6='Mini Tower'; 7='Tower';
                  8='Portable'; 9='Laptop'; 10='Notebook'; 11='Hand Held'; 13='All in One';
                  14='Sub Notebook'; 15='Space-saving'; 16='Lunch Box'; 17='Main System Chassis';
                  23='Rack Mount Chassis'; 30='Tablet'; 31='Convertible'; 32='Detachable' }
        if ($chassis.Count -gt 0 -and $map.ContainsKey($chassis[0])) { $chassisName = $map[$chassis[0]] }
    } catch { }

    # Chassi é mal preenchido por vários fabricantes. Bateria é a confirmação secundária.
    $portableCodes = @(8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32)
    # O @() e obrigatorio: com um unico codigo de chassi, o Where-Object devolve escalar
    # e .Count em escalar lanca excecao sob Set-StrictMode -Version 2.0.
    $chassisSaysLaptop = @($chassis | Where-Object { $portableCodes -contains $_ }).Count -gt 0
    $hasBattery = @(Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue).Count -gt 0

    if ($chassis.Count -eq 0) {
        $isLaptop = if ($hasBattery) { $true } else { $null }
        $basis    = if ($hasBattery) { 'battery' } else { $null }
    } elseif ($chassisSaysLaptop -eq $hasBattery) {
        $isLaptop = $chassisSaysLaptop; $basis = 'both'
    } else {
        # Discordância: bateria vence. Desktop com bateria é raro; notebook sem, não.
        $isLaptop = $hasBattery; $basis = 'conflict'
    }

    $biosDate = $null; $ageYears = $null
    $bd = Prop $bios 'ReleaseDate'
    if ($bd -is [datetime]) {
        $biosDate = $bd.ToString('yyyy-MM-dd')
        $ageYears = [math]::Round(((Get-Date) - $bd).TotalDays / 365.25, 1)
    }

    [ordered]@{
        hostname      = Prop $cs 'Name'
        domainJoined  = [bool](Prop $cs 'PartOfDomain')
        domain        = if (Prop $cs 'PartOfDomain') { Prop $cs 'Domain' } else { $null }
        workgroup     = if (Prop $cs 'PartOfDomain') { $null } else { Prop $cs 'Workgroup' }
        manufacturer  = Prop $cs 'Manufacturer'
        model         = Prop $cs 'Model'
        uuid          = Prop $csp 'UUID'
        productSerial = Prop $csp 'IdentifyingNumber'
        chassisTypes  = ArrOrNull $chassis
        chassisTypeName = $chassisName
        isLaptop      = $isLaptop
        isLaptopBasis = $basis
        bios = [ordered]@{
            manufacturer = Prop $bios 'Manufacturer'
            version      = Prop $bios 'SMBIOSBIOSVersion'
            serial       = Prop $bios 'SerialNumber'
            releaseDate  = $biosDate
        }
        baseboard = [ordered]@{
            manufacturer = Prop $bb 'Manufacturer'
            product      = Prop $bb 'Product'
            serial       = Prop $bb 'SerialNumber'
        }
        approxAgeYears = $ageYears
        approxAgeBasis = if ($null -ne $ageYears) { 'biosReleaseDate' } else { $null }
    }
} -Summary {
    param($d)
    $tipo = if ($d.isLaptop -eq $true) { 'Notebook' } elseif ($d.isLaptop -eq $false) { 'Desktop' } else { 'Máquina' }
    "$tipo $($d.manufacturer) $($d.model)"
}

# ---------------------------------------------------------------- 2. cpu

Invoke-Collector -Id 'cpu' -DisplayName 'Processador' -RequiresElevation $false -Script {
    $p = @(Get-CimInstance Win32_Processor)[0]
    $raw = Prop $p 'Name'

    # Normalização para casar com a lista oficial de CPUs (ADR-006).
    $norm = $null
    if ($raw) {
        $norm = $raw -replace '\((R|TM|C|r|tm)\)', '' `
                     -replace '(?i)\s*CPU\s*@.*$', '' `
                     -replace '(?i)\s*@\s*[\d.]+\s*GHz.*$', '' `
                     -replace '(?i)^\d+(st|nd|rd|th)\s+Gen\s+', '' `
                     -replace '(?i)\s+with\s+Radeon.*$', '' `
                     -replace '\s+', ' '
        $norm = $norm.Trim()
    }

    [ordered]@{
        name              = $raw
        normalizedName    = $norm
        manufacturer      = Prop $p 'Manufacturer'
        physicalCores     = Prop $p 'NumberOfCores'
        logicalProcessors = Prop $p 'NumberOfLogicalProcessors'
        maxClockMhz       = Prop $p 'MaxClockSpeed'
        socket            = Prop $p 'SocketDesignation'
        architecture      = switch (Prop $p 'AddressWidth') { 64 { 'x64' } 32 { 'x86' } default { $null } }
        virtualizationFirmwareEnabled = Prop $p 'VirtualizationFirmwareEnabled'
        # A lista oficial ainda não está embutida (ADR-006). null + basis explicam
        # que não foi avaliado — nunca "não suportado".
        win11Supported      = $null
        win11SupportBasis   = 'listMissing'
    }
} -Summary { param($d) "$($d.name), $($d.physicalCores) núcleos" }

# ---------------------------------------------------------------- 3. memória

Invoke-Collector -Id 'memory' -DisplayName 'Memória' -RequiresElevation $false -Script {
    $cs = Get-CimInstance Win32_ComputerSystem
    $mods = @(Get-CimInstance Win32_PhysicalMemory)
    $arr  = @(Get-CimInstance Win32_PhysicalMemoryArray -ErrorAction SilentlyContinue)

    $totalBytes = Prop $cs 'TotalPhysicalMemory'
    $totalGiB = if ($null -ne $totalBytes) { [int][math]::Round($totalBytes / 1GB, 0) } else { $null }

    $totalSlots = if ($arr.Count -gt 0) { Prop $arr[0] 'MemoryDevices' } else { $null }
    $usedSlots = $mods.Count
    $freeSlots = if ($null -ne $totalSlots -and $totalSlots -ge $usedSlots) { $totalSlots - $usedSlots } else { $null }

    # MaxCapacity é mal preenchido por vários fabricantes. Zero ou absurdo vira null.
    $maxCap = $null
    if ($arr.Count -gt 0) {
        $mc = Prop $arr[0] 'MaxCapacityEx'
        if ($null -eq $mc) { $mc = Prop $arr[0] 'MaxCapacity' }
        if ($null -ne $mc -and $mc -gt 0) {
            $bytes = [int64]$mc * 1024
            if ($bytes -ge 1GB -and $bytes -le 8TB) { $maxCap = $bytes }
        }
    }

    $typeMap = @{ 20='DDR'; 21='DDR2'; 24='DDR3'; 26='DDR4'; 34='DDR5'; 35='LPDDR4'; 36='LPDDR5' }
    $list = @()
    foreach ($m in $mods) {
        $code = Prop $m 'SMBIOSMemoryType'
        $src = 'SMBIOSMemoryType'
        if ($null -eq $code -or $code -eq 0) { $code = Prop $m 'MemoryType'; $src = 'MemoryType' }
        $list += [ordered]@{
            capacityBytes      = Prop $m 'Capacity'
            speedMhz           = Prop $m 'Speed'
            configuredSpeedMhz = Prop $m 'ConfiguredClockSpeed'
            manufacturer       = Prop $m 'Manufacturer'
            partNumber         = if (Prop $m 'PartNumber') { (Prop $m 'PartNumber').Trim() } else { $null }
            bankLabel          = Prop $m 'BankLabel'
            deviceLocator      = Prop $m 'DeviceLocator'
            memoryTypeCode     = $code
            memoryTypeName     = if ($null -ne $code -and $typeMap.ContainsKey([int]$code)) { $typeMap[[int]$code] } else { $null }
            memoryTypeSource   = if ($null -ne $code) { $src } else { $null }
        }
    }

    $speeds = @($list | ForEach-Object { $_.speedMhz } | Where-Object { $null -ne $_ } | Select-Object -Unique)

    [ordered]@{
        totalBytes       = $totalBytes
        totalGiB         = $totalGiB
        totalSlots       = $totalSlots
        usedSlots        = $usedSlots
        freeSlots        = $freeSlots
        maxCapacityBytes = $maxCap
        speedMismatch    = if ($speeds.Count -gt 0) { $speeds.Count -gt 1 } else { $null }
        modules          = ArrOrNull $list
    }
} -Summary {
    param($d)
    $slots = if ($null -ne $d.freeSlots) { "$($d.freeSlots) slots livres" } else { 'slots não verificados' }
    "$(Format-Bytes $d.totalBytes) em $($d.usedSlots) pente(s), $slots"
}

# ---------------------------------------------------------------- 4. armazenamento

Invoke-Collector -Id 'storage' -DisplayName 'Armazenamento e saúde de disco' -RequiresElevation $false -Script {
    $sysDrive = $env:SystemDrive

    # Tipo de mídia SÓ vem daqui. Win32_DiskDrive.MediaType devolve "Fixed hard disk
    # media" também para SSD — armadilha clássica, proibida pelo doc 02 §4.3.
    $msft = @(); $msftErr = $null
    try { $msft = @(Get-CimInstance -Namespace 'root\Microsoft\Windows\Storage' -ClassName MSFT_PhysicalDisk -ErrorAction Stop) }
    catch { $msftErr = $_.Exception.Message }

    $smart = @{}
    try {
        foreach ($s in @(Get-CimInstance -Namespace 'root\wmi' -ClassName MSStorageDriver_FailurePredictStatus -ErrorAction Stop)) {
            $smart[[string](Prop $s 'InstanceName')] = [bool](Prop $s 'PredictFailure')
        }
    } catch { }

    $mediaMap = @{ 3='HDD'; 4='SSD'; 5='SCM' }
    $busMap = @{ 1='SCSI'; 2='ATAPI'; 3='ATA'; 4='1394'; 5='SSA'; 6='Fibre Channel'; 7='USB';
                 8='RAID'; 9='iSCSI'; 10='SAS'; 11='SATA'; 12='SD'; 13='MMC'; 17='NVMe' }
    $healthMap = @{ 0='Healthy'; 1='Warning'; 2='Unhealthy' }

    $disks = @()
    foreach ($d in $msft) {
        $mt = Prop $d 'MediaType'
        # DeviceId do MSFT_PhysicalDisk vem STRING ("0"), e o schema exige inteiro.
        # TryParse em vez de cast direto: se vier algo inesperado, fica null em vez
        # de estourar o coletor inteiro por causa do indice.
        $idx = $null; $idxParsed = 0
        if ([int]::TryParse([string](Prop $d 'DeviceId'), [ref]$idxParsed)) { $idx = $idxParsed }
        $disks += [ordered]@{
            index           = $idx
            model           = Prop $d 'FriendlyName'
            serial          = if (Prop $d 'SerialNumber') { (Prop $d 'SerialNumber').Trim() } else { $null }
            sizeBytes       = Prop $d 'Size'
            interfaceType   = $null
            mediaType       = if ($null -ne $mt -and $mediaMap.ContainsKey([int]$mt)) { $mediaMap[[int]$mt] } else { 'Unknown' }
            mediaTypeSource = if ($msft.Count) { 'MSFT_PhysicalDisk' } else { 'unavailable' }
            busType         = $(if ($null -ne (Prop $d 'BusType') -and $busMap.ContainsKey([int](Prop $d 'BusType'))) { $busMap[[int](Prop $d 'BusType')] } else { $null })
            healthStatus    = $(if ($null -ne (Prop $d 'HealthStatus') -and $healthMap.ContainsKey([int](Prop $d 'HealthStatus'))) { $healthMap[[int](Prop $d 'HealthStatus')] } else { 'Unknown' })
            failurePredicted = $null
        }
    }
    if ($disks.Count -eq 0) {
        # Sem MSFT_PhysicalDisk, registramos o disco mas NÃO adivinhamos o tipo de mídia.
        foreach ($d in @(Get-CimInstance Win32_DiskDrive)) {
            $disks += [ordered]@{
                index = Prop $d 'Index'; model = Prop $d 'Model'
                serial = if (Prop $d 'SerialNumber') { (Prop $d 'SerialNumber').Trim() } else { $null }
                sizeBytes = Prop $d 'Size'; interfaceType = Prop $d 'InterfaceType'
                mediaType = 'Unknown'; mediaTypeSource = 'unavailable'
                busType = $null; healthStatus = 'Unknown'; failurePredicted = $null
            }
        }
    }
    if ($smart.Count -eq 1 -and $disks.Count -eq 1) {
        $disks[0].failurePredicted = @($smart.Values)[0]
    }

    $vols = @()
    foreach ($v in @(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3')) {
        $size = Prop $v 'Size'; $free = Prop $v 'FreeSpace'
        $vols += [ordered]@{
            driveLetter    = Prop $v 'DeviceID'
            label          = Prop $v 'VolumeName'
            fileSystem     = Prop $v 'FileSystem'
            sizeBytes      = $size
            freeBytes      = $free
            freePercent    = if ($size -and $size -gt 0 -and $null -ne $free) { [math]::Round(($free / $size) * 100, 1) } else { $null }
            isSystemVolume = ((Prop $v 'DeviceID') -eq $sysDrive)
        }
    }

    $sysVol = $vols | Where-Object { $_.isSystemVolume } | Select-Object -First 1

    # Disco de sistema = o disco físico que hospeda o volume de sistema.
    $sysDisk = $null
    try {
        $part = Get-CimInstance -Namespace 'root\Microsoft\Windows\Storage' -ClassName MSFT_Partition -ErrorAction Stop |
                Where-Object { $_.DriveLetter -eq $sysDrive.TrimEnd(':') } | Select-Object -First 1
        if ($part) {
            $sysDisk = $disks | Where-Object { "$($_.index)" -eq "$($part.DiskNumber)" } | Select-Object -First 1
        }
    } catch { }
    if ($null -eq $sysDisk -and $disks.Count -eq 1) { $sysDisk = $disks[0] }

    $partStyle = $null
    try {
        $md = Get-CimInstance -Namespace 'root\Microsoft\Windows\Storage' -ClassName MSFT_Disk -ErrorAction Stop |
              Where-Object { $_.IsSystem -eq $true } | Select-Object -First 1
        if ($md) { $partStyle = switch ([int]$md.PartitionStyle) { 1 { 'MBR' } 2 { 'GPT' } default { 'Unknown' } } }
    } catch { }

    # TRIM. A sonda confirmou que "fsutil behavior query" responde SEM elevação — só
    # "behavior set" exige. A saída é LOCALIZADA, então o padrão ancora no número e nunca
    # na prosa; em pt-BR a linha vem como:
    #   "NTFS DisableDeleteNotify = 0  (Permite que operações TRIM sejam enviadas ...)"
    # Windows mais antigo devolve só "DisableDeleteNotify = 0", sem o prefixo do sistema
    # de arquivos — daí o segundo padrão. ReFS tem linha própria e é ignorada de propósito.
    $trim = $null
    try {
        $fsu = (& fsutil.exe behavior query DisableDeleteNotify 2>&1 | Out-String)
        $m = [regex]::Match($fsu, '(?im)^\s*NTFS\s+DisableDeleteNotify\s*=\s*(\d+)')
        if (-not $m.Success) { $m = [regex]::Match($fsu, '(?im)^\s*DisableDeleteNotify\s*=\s*(\d+)') }
        # 0 = notificação de exclusão HABILITADA, ou seja TRIM ligado. A polaridade é
        # invertida no nome da chave — ler como "TRIM desabilitado?" leva a erro.
        if ($m.Success) { $trim = ([int]$m.Groups[1].Value -eq 0) }
    } catch { }

    $winOld = Join-Path $sysDrive 'Windows.old'

    [ordered]@{
        physicalDisks = ArrOrNull $disks
        volumes       = ArrOrNull $vols
        systemDisk = if ($sysDisk) {
            [ordered]@{
                model = $sysDisk.model; sizeBytes = $sysDisk.sizeBytes
                mediaType = $sysDisk.mediaType; busType = $sysDisk.busType
                healthStatus = $sysDisk.healthStatus; failurePredicted = $sysDisk.failurePredicted
                trimEnabled = $trim          # medido via fsutil; null se o padrão não casar
                fragmentationPercent = $null # análise de volume é lenta demais para os 90 s
                partitionStyle = $partStyle
            }
        } else { $null }
        systemVolume = if ($sysVol) {
            [ordered]@{ driveLetter = $sysVol.driveLetter; sizeBytes = $sysVol.sizeBytes
                        freeBytes = $sysVol.freeBytes; freePercent = $sysVol.freePercent }
        } else { $null }
        windowsOldPresent   = (Test-Path $winOld)
        windowsOldSizeBytes = $null
    }
} -Summary {
    param($d)
    if (-not $d.systemDisk) { return 'Disco de sistema não identificado' }
    $t = $d.systemDisk.mediaType
    $f = if ($d.systemVolume) { "$($d.systemVolume.freePercent)% livre" } else { 'espaço não verificado' }
    "Disco de sistema: $t $(Format-Bytes $d.systemDisk.sizeBytes), $f"
}

# ---------------------------------------------------------------- 5. dispositivos

Invoke-Collector -Id 'devices' -DisplayName 'Placa de vídeo e dispositivos' -RequiresElevation $false -Script {
    $vids = @()
    foreach ($v in @(Get-CimInstance Win32_VideoController)) {
        $dd = Prop $v 'DriverDate'
        $vids += [ordered]@{
            name              = Prop $v 'Name'
            driverVersion     = Prop $v 'DriverVersion'
            driverDate        = if ($dd -is [datetime]) { $dd.ToString('yyyy-MM-dd') } else { $null }
            adapterRamBytes   = Prop $v 'AdapterRAM'
            currentResolution = if ((Prop $v 'CurrentHorizontalResolution') -and (Prop $v 'CurrentVerticalResolution')) {
                '{0}x{1}' -f (Prop $v 'CurrentHorizontalResolution'), (Prop $v 'CurrentVerticalResolution')
            } else { $null }
        }
    }

    $problems = @()
    foreach ($p in @(Get-CimInstance Win32_PnPEntity -Filter 'ConfigManagerErrorCode <> 0')) {
        $problems += [ordered]@{
            name                   = Prop $p 'Name'
            deviceClass            = Prop $p 'PNPClass'
            configManagerErrorCode = Prop $p 'ConfigManagerErrorCode'
            errorDescription       = switch ([int](Prop $p 'ConfigManagerErrorCode')) {
                10 { 'O dispositivo não pode ser iniciado' }
                22 { 'O dispositivo está desabilitado' }
                28 { 'Os drivers deste dispositivo não estão instalados' }
                31 { 'O dispositivo não está funcionando corretamente' }
                43 { 'O Windows parou este dispositivo por relatar problemas' }
                default { $null }
            }
        }
    }

    [ordered]@{
        videoControllers   = ArrOrNull $vids
        problemDevices     = $problems
        problemDeviceCount = $problems.Count
    }
} -Summary {
    param($d)
    $g = if ($d.videoControllers) { $d.videoControllers[0].name } else { 'vídeo não identificado' }
    "$g, $($d.problemDeviceCount) dispositivo(s) com problema"
}

# ---------------------------------------------------------------- 6. sistema operacional

Invoke-Collector -Id 'os' -DisplayName 'Sistema operacional e licenciamento' -RequiresElevation $false -Script {
    $os = Get-CimInstance Win32_OperatingSystem
    $cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction SilentlyContinue

    $build = [int](Prop $os 'BuildNumber')
    $isServer = ((Prop $os 'ProductType') -ne 1)
    $caption = Prop $os 'Caption'

    # Normalizado a partir da BUILD, não do caption — caption é localizado.
    $family = if ($isServer) { 'Windows Server' }
        elseif ($build -ge 22000) { 'Windows 11' }
        elseif ($build -ge 10240) { 'Windows 10' }
        elseif ($build -ge 9600)  { 'Windows 8.1' }
        elseif ($build -ge 9200)  { 'Windows 8' }
        elseif ($build -ge 7600)  { 'Windows 7' }
        elseif ($build -gt 0)     { 'Older' }
        else                      { 'Unknown' }

    # Edição também vem do registro, não do caption traduzido.
    $editionId = Prop $cv 'EditionID'
    $isHome = if ($editionId) { $editionId -match '(?i)core|home' } else { $null }

    $inst = Prop $os 'InstallDate'
    $boot = Prop $os 'LastBootUpTime'

    # SoftwareLicensingProduct é lenta e exige elevação. Sem elevação, fica Unknown —
    # que é honesto, não é "não ativado".
    $act = [ordered]@{ status = 'Unknown'; statusCode = $null; channel = $null }
    try {
        # NOTA DE AUDITORIA: PartialProductKey aparece apenas na cláusula WHERE, como
        # FILTRO — é o que distingue a licença do Windows instalado das dezenas de
        # outras linhas que a classe devolve. A lista do SELECT tem só LicenseStatus e
        # ProductKeyChannel. Nenhum fragmento de chave é lido, gravado ou registrado
        # em log. A proibição do doc 01 §7.1 é sobre COLETAR chave, e não coletamos.
        # Filtrar na consulta, nunca em memória: a classe é lenta.
        $lic = Get-CimInstance -Query "SELECT LicenseStatus, ProductKeyChannel FROM SoftwareLicensingProduct WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL" -ErrorAction Stop |
               Select-Object -First 1
        if ($lic) {
            $code = [int]$lic.LicenseStatus
            $act.statusCode = $code
            $act.channel = $lic.ProductKeyChannel
            $act.status = switch ($code) {
                0 { 'Unlicensed' } 1 { 'Licensed' } 2 { 'OutOfBox' } 3 { 'OutOfTolerance' }
                4 { 'NonGenuine' } 5 { 'Notification' } default { 'Unknown' }
            }
        }
    } catch { }

    [ordered]@{
        caption        = $caption
        edition        = $editionId
        productFamily  = $family
        isServer       = $isServer
        isHomeEdition  = $isHome
        version        = Prop $os 'Version'
        buildNumber    = $build
        ubr            = Prop $cv 'UBR'
        displayVersion = $(if (Prop $cv 'DisplayVersion') { Prop $cv 'DisplayVersion' } else { Prop $cv 'ReleaseId' })
        architecture   = Prop $os 'OSArchitecture'
        installDate    = if ($inst -is [datetime]) { $inst.ToString('yyyy-MM-dd') } else { $null }
        installAgeYears = if ($inst -is [datetime]) { [math]::Round(((Get-Date) - $inst).TotalDays / 365.25, 1) } else { $null }
        lastBootTime   = if ($boot -is [datetime]) { $boot.ToString('o') } else { $null }
        uptimeDays     = if ($boot -is [datetime]) { [math]::Round(((Get-Date) - $boot).TotalDays, 2) } else { $null }
        activation     = $act
        # ADR-005: tabela de builds vazia, então NÃO avaliamos. OS-005 resolve Indeterminate.
        buildFreshness = [ordered]@{
            evaluated = $false
            reason = 'rules/windows-builds.json não preenchido — ver ADR-005'
            latestKnownBuild = $null; latestKnownUbr = $null
            tableValidUntil = $null; isCurrent = $null
        }
    }
} -Summary {
    param($d)
    $a = switch ($d.activation.status) { 'Licensed' { 'ativado' } 'Unknown' { 'ativação não verificada' } default { 'NÃO ativado' } }
    "$($d.caption) $($d.displayVersion), $a"
}

# ---------------------------------------------------------------- 7. atualizações

Invoke-Collector -Id 'updates' -DisplayName 'Atualizações do Windows' -RequiresElevation $false -Script {
    $hf = @()
    foreach ($h in @(Get-CimInstance Win32_QuickFixEngineering)) {
        $d = Prop $h 'InstalledOn'
        $hf += [ordered]@{
            hotfixId    = Prop $h 'HotFixID'
            installedOn = if ($d -is [datetime]) { $d.ToString('yyyy-MM-dd') } else { $null }
            description = Prop $h 'Description'
        }
    }
    $dates = @($hf | ForEach-Object { $_.installedOn } | Where-Object { $_ } | Sort-Object -Descending)
    $last = if ($dates.Count) { $dates[0] } else { $null }

    $wuEnabled = $null
    try {
        $svc = Get-CimInstance Win32_Service -Filter "Name='wuauserv'" -ErrorAction Stop
        if ($svc) { $wuEnabled = ((Prop $svc 'StartMode') -ne 'Disabled') }
    } catch { }

    $wsus = $null
    try {
        $wsus = [bool](Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' -Name WUServer -ErrorAction Stop)
    } catch { $wsus = $false }

    [ordered]@{
        hotfixes            = ArrOrNull $hf
        # SEMPRE true: a classe não lista atualizações cumulativas modernas.
        coverageIsPartial   = $true
        lastUpdateDate      = $last
        daysSinceLastUpdate = if ($last) { [int]((Get-Date) - [datetime]$last).TotalDays } else { $null }
        windowsUpdateServiceEnabled = $wuEnabled
        wsusConfigured      = $wsus
    }
} -Summary {
    param($d)
    if ($null -eq $d.daysSinceLastUpdate) { return 'Nenhuma atualização registrada' }
    "Última atualização registrada há $($d.daysSinceLastUpdate) dias"
}

# ---------------------------------------------------------------- 8. Windows 11

# RequiresElevation $false: MEDIDO EM CAMPO que só o Win32_Tpm exige privilégio. Secure
# Boot (registro) e modo de firmware respondem sem elevação. Gatear o coletor inteiro
# perdia W11-004 e W11-005 de graça. O TPM degrada sozinho para null no seu try/catch.
Invoke-Collector -Id 'win11' -DisplayName 'Compatibilidade com Windows 11' -RequiresElevation $false -Script {
    $tpm = [ordered]@{ present = $null; specVersionRaw = $null; majorVersion = $null; enabled = $null; activated = $null }
    try {
        $t = Get-CimInstance -Namespace 'root\CIMV2\Security\MicrosoftTpm' -ClassName Win32_Tpm -ErrorAction Stop | Select-Object -First 1
        if ($t) {
            $tpm.present = $true
            $spec = Prop $t 'SpecVersion'
            $tpm.specVersionRaw = $spec
            if ($spec) {
                $first = ($spec -split ',')[0].Trim()
                $m = [regex]::Match($first, '^\d+(\.\d+)?')
                if ($m.Success) { $tpm.majorVersion = [double]$m.Value }
            }
            $tpm.enabled   = Prop $t 'IsEnabled_InitialValue'
            $tpm.activated = Prop $t 'IsActivated_InitialValue'
        } else {
            # Namespace respondeu e não devolveu instância: ausência confirmada.
            $tpm.present = $false
        }
    } catch {
        # Namespace inacessível: NÃO é ausência de TPM. Fica null → Indeterminate.
        $tpm.present = $null
    }

    $sb = [ordered]@{ enabled = $null; supported = $null; source = 'unavailable' }
    try {
        $v = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State' -Name UEFISecureBootEnabled -ErrorAction Stop).UEFISecureBootEnabled
        $sb.enabled = ([int]$v -eq 1); $sb.supported = $true; $sb.source = 'registry'
    } catch {
        # Chave ausente normalmente significa firmware sem suporte a Secure Boot.
        $sb.supported = $false
    }

    $fw = [ordered]@{ mode = 'Unknown'; detectionMethod = 'unavailable' }
    if ($env:firmware_type) {
        $fw.mode = switch -Regex ($env:firmware_type) { '(?i)uefi' { 'UEFI' } '(?i)legacy' { 'Legacy' } default { 'Unknown' } }
        if ($fw.mode -ne 'Unknown') { $fw.detectionMethod = 'firmware_type' }
    }
    if ($fw.mode -eq 'Unknown') {
        try {
            $md = Get-CimInstance -Namespace 'root\Microsoft\Windows\Storage' -ClassName MSFT_Disk -ErrorAction Stop |
                  Where-Object { $_.IsSystem -eq $true } | Select-Object -First 1
            if ($md) {
                $fw.mode = if ([int]$md.PartitionStyle -eq 2) { 'UEFI' } else { 'Legacy' }
                $fw.detectionMethod = 'partitionStyle'
            }
        } catch { }
    }

    [ordered]@{
        tpm = $tpm; secureBoot = $sb; firmware = $fw
        requirements = [ordered]@{ cpu='Unknown'; tpm='Unknown'; secureBoot='Unknown'; firmware='Unknown'; ram='Unknown'; storage='Unknown' }
        eligible = $null; blockers = @(); unknowns = @()
    }
} -Summary {
    param($d)
    if ($d.tpm.present -eq $false) { return 'TPM não detectado' }
    if ($null -eq $d.tpm.present) { return 'TPM não pôde ser verificado' }
    "TPM $($d.tpm.majorVersion), firmware $($d.firmware.mode)"
}

# ---------------------------------------------------------------- 9. segurança

# RequiresElevation $false: MEDIDO EM CAMPO que só o Win32_EncryptableVolume exige
# privilégio. Firewall, RDP, SMBv1 e UAC respondem sem elevação — e eram perdidos de
# graça (SEC-006, SEC-008, SEC-009). Cada sub-leitura tem try/catch próprio; o BitLocker
# degrada para null e SEC-004/005 resolvem Indeterminate, que é o correto.
Invoke-Collector -Id 'security' -DisplayName 'Segurança e criptografia' -RequiresElevation $false -Script {
    $sysDrive = $env:SystemDrive

    $bl = [ordered]@{ available = $null; systemVolumeProtected = $null; volumes = $null }
    try {
        $vols = @(Get-CimInstance -Namespace 'root\CIMV2\Security\MicrosoftVolumeEncryption' -ClassName Win32_EncryptableVolume -ErrorAction Stop)
        $bl.available = $true
        $list = @()
        foreach ($v in $vols) {
            $ps = Prop $v 'ProtectionStatus'
            $list += [ordered]@{
                driveLetter      = Prop $v 'DriveLetter'
                protectionStatus = switch ([int]$ps) { 0 { 'Off' } 1 { 'On' } default { 'Unknown' } }
                conversionStatus = switch ([int](Prop $v 'ConversionStatus')) {
                    0 { 'FullyDecrypted' } 1 { 'FullyEncrypted' } 2 { 'EncryptionInProgress' }
                    3 { 'DecryptionInProgress' } default { $null } }
                encryptionMethod = switch ([int](Prop $v 'EncryptionMethod')) {
                    0 { $null } 3 { 'Aes128' } 4 { 'Aes256' } 6 { 'XtsAes128' } 7 { 'XtsAes256' } default { $null } }
            }
        }
        $bl.volumes = ArrOrNull $list
        $sys = $list | Where-Object { $_.driveLetter -eq $sysDrive } | Select-Object -First 1
        if ($sys) { $bl.systemVolumeProtected = ($sys.protectionStatus -eq 'On') }
    } catch {
        # A sonda mediu que o namespace EXISTE em Windows 11 Home — a premissa de que só
        # Pro o tem estava errada. Então distinguir os dois erros passa a importar:
        #   acesso negado (sessão sem elevação) → null, não sabemos
        #   qualquer outro (namespace ausente)  → false, capacidade ausente de fato
        # Marcar false por falta de privilégio faria a ausência de criptografia parecer
        # medida quando só houve falta de permissão.
        if ($_.CategoryInfo.Category -eq 'PermissionDenied') { $bl.available = $null }
        else { $bl.available = $false }
    }

    $fw = [ordered]@{ anyProfileDisabled = $null; profiles = $null }
    try {
        $profs = @(Get-CimInstance -Namespace 'root\StandardCimv2' -ClassName MSFT_NetFirewallProfile -ErrorAction Stop)
        $list = @()
        foreach ($p in $profs) {
            $e = Prop $p 'Enabled'
            $list += [ordered]@{ name = Prop $p 'Name'; enabled = if ($null -ne $e) { ([int]$e -eq 1) } else { $null } }
        }
        if ($list.Count) {
            $fw.profiles = $list
            $known = @($list | Where-Object { $null -ne $_.enabled })
            if ($known.Count -eq $list.Count) {
                $fw.anyProfileDisabled = (@($list | Where-Object { $_.enabled -eq $false }).Count -gt 0)
            }
        }
    } catch { }

    $rdp = [ordered]@{ enabled = $null; nlaRequired = $null; port = $null }
    try {
        $deny = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -Name fDenyTSConnections -ErrorAction Stop).fDenyTSConnections
        $rdp.enabled = ([int]$deny -eq 0)
        if ($rdp.enabled) {
            try {
                $k = 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp'
                $rdp.nlaRequired = ([int](Get-ItemProperty $k -Name UserAuthentication -ErrorAction Stop).UserAuthentication -eq 1)
                $rdp.port = [int](Get-ItemProperty $k -Name PortNumber -ErrorAction Stop).PortNumber
            } catch { }
        }
    } catch { }

    $smb1 = [ordered]@{ enabled = $null; featureState = $null }
    try {
        $f = Get-CimInstance Win32_OptionalFeature -Filter "Name='SMB1Protocol'" -ErrorAction Stop | Select-Object -First 1
        if ($f) {
            $st = [int](Prop $f 'InstallState')
            $smb1.featureState = switch ($st) { 1 { 'Enabled' } 2 { 'Disabled' } 3 { 'Absent' } default { $null } }
            if ($st -eq 1) { $smb1.enabled = $true } elseif ($st -in @(2,3)) { $smb1.enabled = $false }
        }
    } catch { }

    $uac = [ordered]@{ enabled = $null; consentPromptLevel = $null }
    try {
        $k = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction Stop
        $uac.enabled = ([int](Prop $k 'EnableLUA') -eq 1)
        $uac.consentPromptLevel = Prop $k 'ConsentPromptBehaviorAdmin'
    } catch { }

    [ordered]@{ bitlocker = $bl; firewall = $fw; rdp = $rdp; smb1 = $smb1; uac = $uac }
} -Summary {
    param($d)
    $b = switch ($d.bitlocker.systemVolumeProtected) { $true { 'BitLocker ativo' } $false { 'sem BitLocker' } default { 'BitLocker não verificado' } }
    $f = switch ($d.firewall.anyProfileDisabled) { $true { 'firewall desativado em algum perfil' } $false { 'firewall ativo' } default { 'firewall não verificado' } }
    "$b, $f"
}

# ---------------------------------------------------------------- 10. antivírus

# RequiresElevation $false: MEDIDO EM CAMPO que NENHUMA fonte deste coletor exige
# privilégio — root\SecurityCenter2 e MSFT_MpComputerStatus respondem sem elevação. O
# gate anterior descartava SEC-001/002/003 e SW-005, a família mais valiosa do relatório,
# em toda visita em que o técnico não conseguisse elevar.
Invoke-Collector -Id 'antivirus' -DisplayName 'Antivírus' -RequiresElevation $false -Script {
    $available = $null; $products = @()
    try {
        $raw = @(Get-CimInstance -Namespace 'root\SecurityCenter2' -ClassName AntiVirusProduct -ErrorAction Stop)
        $available = $true
        foreach ($p in $raw) {
            $state = Prop $p 'productState'
            $products += [ordered]@{
                displayName     = Prop $p 'displayName'
                # SEMPRE preservado cru. É o que permite reinterpretar relatórios
                # antigos quando a decodificação melhorar.
                productStateRaw = if ($null -ne $state) { [int]$state } else { $null }
                productStateHex = if ($null -ne $state) { '0x{0:X}' -f [int]$state } else { $null }
                timestamp       = Prop $p 'timestamp'
                # productState é máscara de bits NÃO documentada pela Microsoft.
                # Toda decodificação que circula é engenharia reversa da comunidade.
                # Enquanto a Fase 1 não validar a decodificação contra dezenas de
                # máquinas reais, a confiança é None e SEC-001/002/003 resolvem
                # Indeterminate. Requisito vinculante do doc 03 §4.6.
                interpretation = [ordered]@{
                    confidence = 'None'; enabled = 'Unknown'
                    realtimeProtection = 'Unknown'; definitions = 'Unknown'
                }
            }
        }
    } catch {
        # Namespace não existe em edições Server.
        $available = $false
    }

    $def = [ordered]@{ present = $null; amServiceEnabled = $null; realtimeProtectionEnabled = $null
                       antivirusSignatureAgeDays = $null; isTamperProtected = $null }
    try {
        $s = Get-CimInstance -Namespace 'root\Microsoft\Windows\Defender' -ClassName MSFT_MpComputerStatus -ErrorAction Stop | Select-Object -First 1
        if ($s) {
            $def.present = $true
            $def.amServiceEnabled = Prop $s 'AMServiceEnabled'
            $def.realtimeProtectionEnabled = Prop $s 'RealTimeProtectionEnabled'
            $def.antivirusSignatureAgeDays = Prop $s 'AntivirusSignatureAge'
            $def.isTamperProtected = Prop $s 'IsTamperProtected'
        }
    } catch { }

    [ordered]@{
        securityCenterAvailable = $available
        products                = ArrOrEmpty $products
        defender                = $def
        # Preenchidos na consolidação, que enxerga o coletor de software.
        securitySoftwareInInventory = $null
        anyProtectionDetected   = $null
        activeProductCount      = if ($available) { $products.Count } else { $null }
        overallConfidence       = if ($products.Count) { 'None' } else { $null }
        realtimeProtectionState = 'Unknown'
        definitionsState        = 'Unknown'
    }
} -Summary {
    param($d)
    if ($d.securityCenterAvailable -ne $true) { return 'Central de Segurança indisponível' }
    if (@($d.products).Count -eq 0) { return 'Nenhum produto registrado na Central de Segurança' }
    "$(@($d.products).Count) produto(s): $((@($d.products) | ForEach-Object { $_.displayName }) -join ', ')"
}

# ---------------------------------------------------------------- 11. software

Invoke-Collector -Id 'software' -DisplayName 'Software instalado' -RequiresElevation $false -Script {
    # PROIBIDO usar Win32_Product: dispara reconfiguração de pacotes MSI na máquina
    # do cliente. Proibição do doc 02 §4.7, não preferência de performance.
    $keys = @(
        @{ path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'; scope = 'HKLM' },
        @{ path = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'; scope = 'HKLM-WOW6432' },
        @{ path = 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'; scope = 'HKCU' }
    )
    $progs = @()
    foreach ($k in $keys) {
        if (-not (Test-Path $k.path)) { continue }
        foreach ($sub in Get-ChildItem $k.path -ErrorAction SilentlyContinue) {
            $p = Get-ItemProperty $sub.PSPath -ErrorAction SilentlyContinue
            if ($null -eq $p) { continue }
            $name = Prop $p 'DisplayName'
            if (-not $name) { continue }
            if ((Prop $p 'SystemComponent') -eq 1) { continue }
            $idate = Prop $p 'InstallDate'
            $iso = $null
            if ($idate -and "$idate" -match '^\d{8}$') {
                $iso = '{0}-{1}-{2}' -f "$idate".Substring(0,4), "$idate".Substring(4,2), "$idate".Substring(6,2)
            }
            $size = Prop $p 'EstimatedSize'
            # [pscustomobject] e NAO [ordered] de proposito: Sort-Object -Property do
            # PowerShell 5.1 nao enxerga chave de dicionario como propriedade, e com a
            # chave de ordenacao nula o -Unique abaixo colapsaria a lista inteira em
            # um item — falha silenciosa que destroi o inventario.
            $progs += [pscustomobject][ordered]@{
                displayName        = $name
                displayVersion     = Prop $p 'DisplayVersion'
                publisher          = Prop $p 'Publisher'
                installDate        = $iso
                estimatedSizeBytes = if ($null -ne $size) { [int64]$size * 1024 } else { $null }
                scope              = $k.scope
            }
        }
    }
    $progs = @($progs | Sort-Object displayName -Unique)

    # Classificação por heurística de nome/fabricante. É a classificação, não a lista
    # crua, que gera achado comercial. As listas crescem com o campo.
    function MatchAny { param($Programs, [string[]] $Patterns)
        $hits = @()
        foreach ($p in $Programs) {
            $hay = "$($p.displayName) $($p.publisher)"
            foreach ($pat in $Patterns) { if ($hay -match $pat) { $hits += $p.displayName; break } }
        }
        # Dedupe manual: Select-Object -Unique com entrada vazia nao devolve array vazio
        # no 5.1, e a virgula no return impede que a pipeline desenrole o resultado.
        $uniq = @()
        foreach ($h in $hits) { if ($uniq -notcontains $h) { $uniq += $h } }
        return ,$uniq
    }

    $remote = MatchAny $progs @('(?i)teamviewer','(?i)anydesk','(?i)\bvnc\b','(?i)logmein','(?i)splashtop','(?i)gotoassist','(?i)ammyy','(?i)supremo','(?i)rustdesk','(?i)chrome remote desktop')
    $edr    = MatchAny $progs @('(?i)crowdstrike','(?i)sentinelone','(?i)sophos','(?i)carbon black','(?i)cylance','(?i)cortex xdr','(?i)defender for endpoint','(?i)huntress','(?i)\bs1\b agent')
    $av     = MatchAny $progs @('(?i)bitdefender','(?i)\beset\b','(?i)kaspersky','(?i)trend micro','(?i)mcafee','(?i)symantec','(?i)norton','(?i)avast','(?i)avg ','(?i)malwarebytes','(?i)panda security','(?i)f-secure','(?i)webroot')
    $backup = MatchAny $progs @('(?i)veeam','(?i)acronis','(?i)datto','(?i)backup exec','(?i)macrium','(?i)cobian','(?i)urbackup','(?i)carbonite','(?i)idrive','(?i)\bveritas\b')
    $obsol  = MatchAny $progs @('(?i)java\s*(se\s*)?(runtime\s*)?(environment\s*)?[1-8]\b','(?i)adobe flash','(?i)adobe shockwave','(?i)microsoft silverlight','(?i)\.net framework [1-3]\.')
    $pup    = MatchAny $progs @('(?i)advanced systemcare','(?i)driver booster','(?i)ccleaner','(?i)pc ?optimizer','(?i)\bdriverpack\b','(?i)wondershare','(?i)mypc','(?i)toolbar','(?i)search protect','(?i)web companion')

    $browsers = @()
    foreach ($b in @(
        @{ n='Google Chrome'; pat='(?i)^google chrome$' },
        @{ n='Mozilla Firefox'; pat='(?i)^mozilla firefox' },
        @{ n='Microsoft Edge'; pat='(?i)^microsoft edge$' },
        @{ n='Opera'; pat='(?i)^opera\b' })) {
        $hit = $progs | Where-Object { $_.displayName -match $b.pat } | Select-Object -First 1
        if ($hit) {
            # latestKnownVersion exigiria tabela mantida, que não existe. Fica null
            # e outdated fica null → SW-003 resolve Indeterminate.
            $browsers += [ordered]@{ name = $b.n; version = $hit.displayVersion
                                     latestKnownVersion = $null; outdated = $null }
        }
    }

    [ordered]@{
        count    = $progs.Count
        programs = $progs
        classification = [ordered]@{
            remoteAccessTools       = $remote
            antivirusProducts       = $av
            edrAgents               = $edr
            backupAgents            = $backup
            obsoleteRuntimes        = $obsol
            potentiallyUnwanted     = $pup
            # Heurística de licenciamento NÃO implementada: exposição jurídica,
            # aguarda revisão do jurídico. Ver SW-004.
            licenseReviewCandidates = @()
        }
        browsers         = ArrOrNull $browsers
        outdatedBrowsers = @()
    }
} -Summary { param($d) "$($d.count) programas instalados" }

# ---------------------------------------------------------------- 12. inicialização

Invoke-Collector -Id 'startup' -DisplayName 'Programas de inicialização' -RequiresElevation $false -Script {
    $items = @()

    foreach ($k in @(
        @{ p='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run';       loc='HKLM-Run' },
        @{ p='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce';   loc='HKLM-RunOnce' },
        @{ p='HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'; loc='HKLM-Run' },
        @{ p='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run';       loc='HKCU-Run' },
        @{ p='HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce';   loc='HKCU-RunOnce' })) {
        if (-not (Test-Path $k.p)) { continue }
        foreach ($prop in (Get-ItemProperty $k.p).PSObject.Properties) {
            if ($prop.Name -like 'PS*') { continue }
            $items += [ordered]@{
                name = $prop.Name; command = "$($prop.Value)"; location = $k.loc
                publisher = $null; signed = $null; enabled = $null
                protected = $null; protectionReason = $null
            }
        }
    }

    foreach ($f in @(
        @{ p = [Environment]::GetFolderPath('Startup');       loc='StartupFolder' },
        @{ p = [Environment]::GetFolderPath('CommonStartup'); loc='CommonStartupFolder' })) {
        if (-not $f.p -or -not (Test-Path $f.p)) { continue }
        foreach ($file in Get-ChildItem -Path $f.p -File -ErrorAction SilentlyContinue) {
            $items += [ordered]@{
                name = $file.BaseName; command = $file.FullName; location = $f.loc
                publisher = $null; signed = $null; enabled = $null
                protected = $null; protectionReason = $null
            }
        }
    }

    # Assinatura e fabricante: alimentam a lista de exclusão da Fase 5.
    foreach ($i in $items) {
        $exe = $null
        if ($i.command -match '^\s*"([^"]+)"') { $exe = $Matches[1] }
        elseif ($i.command -match '^\s*(\S+\.exe)') { $exe = $Matches[1] }
        if ($exe) {
            $exe = [Environment]::ExpandEnvironmentVariables($exe)
            if (Test-Path $exe -PathType Leaf) {
                try {
                    $sig = Get-AuthenticodeSignature -FilePath $exe -ErrorAction Stop
                    $i.signed = ($sig.Status -eq 'Valid')
                    if ($sig.SignerCertificate) {
                        $cn = ($sig.SignerCertificate.Subject -split ',')[0] -replace '^CN=', ''
                        $i.publisher = $cn.Trim('"').Trim()
                    }
                } catch { }
            }
        }
    }

    $tasks = $null
    try {
        # O "$null -ne $_" NÃO é redundante: tarefa sem gatilho tem Triggers = $null, e
        # mandar $null para o pipeline gera UMA iteração com $_ = $null. Aí $_.CimClass
        # lança sob StrictMode 2.0 e o try/catch abaixo engole — o campo saía null em
        # toda máquina, silenciosamente. Medido em campo pela sonda.
        $tasks = @(Get-ScheduledTask -ErrorAction Stop | Where-Object {
            $_.State -ne 'Disabled' -and ($_.Triggers | Where-Object {
                $null -ne $_ -and $_.CimClass.CimClassName -eq 'MSFT_TaskLogonTrigger' })
        }).Count
    } catch { }

    [ordered]@{
        count = $items.Count
        # Win32_StartupCommand e as chaves Run não cobrem tarefas agendadas nem
        # mecanismos modernos. Aceitável para inventário; insuficiente para a Fase 5.
        coverageIsPartial = $true
        items = ArrOrNull $items
        scheduledLogonTaskCount = $tasks
    }
} -Summary { param($d) "$($d.count) programas na inicialização" }

# ---------------------------------------------------------------- 13. rede

Invoke-Collector -Id 'network' -DisplayName 'Rede' -RequiresElevation $false -Script {
    $cfgByIndex = @{}
    foreach ($c in @(Get-CimInstance Win32_NetworkAdapterConfiguration)) { $cfgByIndex[[int]$c.Index] = $c }

    $netByMac = @{}
    try {
        foreach ($n in @(Get-CimInstance -Namespace 'root\StandardCimv2' -ClassName MSFT_NetAdapter -ErrorAction Stop)) {
            if ($n.MacAddress) { $netByMac[($n.MacAddress -replace '-', ':').ToUpper()] = $n }
        }
    } catch { }

    # MEDIDO EM CAMPO: MSFT_NetAdapter NÃO tem PhysicalMediaType, MediaType, LinkSpeed,
    # MacAddress nem Status neste build — todas voltam ausentes. Quem existe e responde é
    # NdisPhysicalMedium (enum NDIS_PHYSICAL_MEDIUM). Valores confirmados pela sonda:
    # 9 = Native802_11 (Wi-Fi) e 14 = 802_3 (Ethernet).
    #
    # Antes disto, 'Wired' era INALCANÇÁVEL: a propriedade lida não existia, o tipo caía em
    # Unknown e só o regex de descrição salvava — e ele só reconhece Wi-Fi. Máquina com cabo
    # saía Unknown e NET-002 perdia a base.
    $ndisWireless = @(1, 8, 9, 12)                       # WirelessLan, WirelessWan, Native802_11, WiMax
    $ndisWired    = @(2, 3, 4, 5, 6, 7, 14, 15, 17, 18)  # CableModem, PhoneLine, PowerLine, DSL, FibreChannel, 1394, 802_3, 802_5, WiredWan, WiredCoWanDsl

    $adapters = @(); $primary = $null
    foreach ($a in @(Get-CimInstance Win32_NetworkAdapter -Filter 'PhysicalAdapter=TRUE')) {
        $mac = Prop $a 'MACAddress'
        $cfg = $cfgByIndex[[int](Prop $a 'Index')]
        $ext = if ($mac) { $netByMac[$mac.ToUpper()] } else { $null }

        $isVirtual = $false
        if ($ext -and $null -ne (Prop $ext 'Virtual')) { $isVirtual = [bool](Prop $ext 'Virtual') }
        $desc = "$(Prop $a 'Name') $(Prop $a 'Description')"
        if ($desc -match '(?i)virtual|hyper-v|vmware|virtualbox|loopback|tap-|tun\b|vpn|wintun|wireguard') { $isVirtual = $true }

        $connType = 'Unknown'
        if ($isVirtual) { $connType = 'Virtual' }
        elseif ($ext) {
            $npm = Prop $ext 'NdisPhysicalMedium'
            if ($null -ne $npm) {
                if ([int]$npm -in $ndisWireless)  { $connType = 'Wireless' }
                elseif ([int]$npm -in $ndisWired) { $connType = 'Wired' }
            }
            # Builds antigos podem expor PhysicalMediaType. Se existir, vale como segunda opinião.
            if ($connType -eq 'Unknown') {
                $pm = Prop $ext 'PhysicalMediaType'
                if ($null -ne $pm) { $connType = if ([int]$pm -in @(9, 16)) { 'Wireless' } else { 'Wired' } }
            }
        }
        # Último recurso, quando o adaptador não casou por MAC com nenhum MSFT_NetAdapter.
        # Wi-Fi primeiro: "Wireless-AC Ethernet Adapter" existe e casaria no padrão de cabo.
        if ($connType -eq 'Unknown' -and $desc -match '(?i)wi-?fi|wireless|802\.11|wlan') { $connType = 'Wireless' }
        elseif ($connType -eq 'Unknown' -and $desc -match '(?i)ethernet|gigabit|\bgbe\b') { $connType = 'Wired' }

        $link = if ($ext) { Prop $ext 'Speed' } else { Prop $a 'Speed' }
        $max  = Prop $a 'MaxSpeed'
        if ($null -eq $max -and $desc -match '(?i)gigabit|gbe|\bi2[12]\d\b') { $max = 1000000000 }

        $connected = ((Prop $a 'NetConnectionStatus') -eq 2)
        $ips = @(); $dns = @(); $gw = $null; $dhcp = $null
        if ($cfg) {
            # O @() envolve o Where-Object INTEIRO: com um unico IPv4 sobrevivendo ao
            # filtro, $ips voltaria escalar e .Count lancaria excecao sob StrictMode 2.0.
            $ips  = @(@(AsArray (Prop $cfg 'IPAddress')) | Where-Object { $_ -notmatch '^fe80|^169\.254' })
            $dns  = @(AsArray (Prop $cfg 'DNSServerSearchOrder'))
            $gw   = @(AsArray (Prop $cfg 'DefaultIPGateway'))[0]
            $dhcp = Prop $cfg 'DHCPEnabled'
        }

        $entry = [ordered]@{
            name = Prop $a 'NetConnectionID'; description = Prop $a 'Description'
            macAddress = $mac; connected = $connected; isVirtual = $isVirtual
            connectionType = $connType
            linkSpeedBps = if ($null -ne $link) { [int64]$link } else { $null }
            maxSpeedBps  = if ($null -ne $max) { [int64]$max } else { $null }
            dhcpEnabled = $dhcp
            ipAddresses = ArrOrNull $ips
            defaultGateway = $gw
            dnsServers = ArrOrNull $dns
        }
        $adapters += $entry
        if ($connected -and -not $isVirtual -and $gw -and -not $primary) { $primary = $entry }
    }

    $downgraded = $null
    if ($primary -and $null -ne $primary.maxSpeedBps -and $null -ne $primary.linkSpeedBps -and $primary.linkSpeedBps -gt 0) {
        $downgraded = ($primary.maxSpeedBps -ge 1000000000 -and $primary.linkSpeedBps -le 100000000)
    }

    $publicDns = $null
    $cs = Get-CimInstance Win32_ComputerSystem
    if ($cs.PartOfDomain -and $primary -and $primary.dnsServers) {
        $publicRanges = @('^8\.8\.', '^8\.8\.4\.', '^1\.1\.1\.1$', '^1\.0\.0\.1$', '^9\.9\.9\.', '^208\.67\.2', '^94\.140\.14\.')
        $publicDns = $false
        foreach ($d in $primary.dnsServers) {
            foreach ($r in $publicRanges) { if ($d -match $r) { $publicDns = $true; break } }
        }
    }

    [ordered]@{
        adapters = ArrOrNull $adapters
        primaryAdapterName = if ($primary) { $primary.name } else { $null }
        primaryConnectionType = if ($primary) { $primary.connectionType } else { $null }
        linkDowngraded = $downgraded
        publicDnsInDomainEnvironment = $publicDns
        staticIpConfigured = if ($primary -and $null -ne $primary.dhcpEnabled) { -not $primary.dhcpEnabled } else { $null }
    }
} -Summary {
    param($d)
    if (-not $d.primaryAdapterName) { return 'Nenhum adaptador ativo identificado' }
    $tipo = switch ($d.primaryConnectionType) { 'Wired' { 'Cabo' } 'Wireless' { 'Wi-Fi' } default { 'Conexão' } }
    $vel = if ($d.adapters) {
        $p = $d.adapters | Where-Object { $_.name -eq $d.primaryAdapterName } | Select-Object -First 1
        if ($p -and $p.linkSpeedBps) { ', ' + [math]::Round($p.linkSpeedBps / 1000000) + ' Mbps' } else { '' }
    } else { '' }
    "$tipo$vel"
}

# ---------------------------------------------------------------- 14. contas

Invoke-Collector -Id 'accounts' -DisplayName 'Contas e privilégios' -RequiresElevation $false -Script {
    # O nome do grupo é LOCALIZADO. Resolver sempre pelo SID conhecido.
    $adminSid = 'S-1-5-32-544'
    $resolvedBySid = $false
    $admins = @()
    try {
        $g = Get-CimInstance Win32_Group -Filter "SID='$adminSid'" -ErrorAction Stop | Select-Object -First 1
        if ($g) {
            $resolvedBySid = $true
            $q = "ASSOCIATORS OF {Win32_Group.Domain='$($g.Domain)',Name='$($g.Name)'} WHERE ResultClass=Win32_Account"
            foreach ($m in @(Get-CimInstance -Query $q -ErrorAction Stop)) {
                $admins += [ordered]@{
                    name = "$(Prop $m 'Domain')\$(Prop $m 'Name')"
                    sid = Prop $m 'SID'
                    domain = Prop $m 'Domain'
                    principalType = switch ([int](Prop $m 'SIDType')) { 1 { 'User' } 2 { 'Group' } default { 'Unknown' } }
                    disabled = Prop $m 'Disabled'
                }
            }
        }
    } catch { }

    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $groupSids = @($id.Groups | ForEach-Object { $_.Value })
    $currentSid = $id.User.Value
    $cs = Get-CimInstance Win32_ComputerSystem

    # SEC-007 é High. Um falso negativo aqui faz a regra dizer "conforme" numa máquina
    # que não é — é a regra 1 violada pelo outro lado, e pior que Indeterminate.
    #
    # NÃO confiar só no token: MEDIDO EM CAMPO (sonda, 2026-07-29, duas rodadas) que o
    # token filtrado do UAC NÃO carrega S-1-5-32-544 numa sessão sem elevação, mesmo
    # para quem é administrador local. Só o token daria isLocalAdmin=false para um admin.
    #
    # Ordem de decisão:
    #   true  — está direto na lista de membros (legível SEM elevação) OU o token traz o
    #           SID do grupo (pega membro indireto, mas só quando elevado)
    #   null  — nenhum dos dois E existe GRUPO entre os membros: a associação pode ser
    #           indireta por esse grupo e não há como saber. Nunca false por ignorância.
    #   false — nenhum dos dois e não há grupo entre os membros
    $directMember   = @($admins | Where-Object { $_.sid -eq $currentSid }).Count -gt 0
    $tokenHasAdmin  = $groupSids -contains $adminSid
    $groupInMembers = @($admins | Where-Object { $_.principalType -eq 'Group' }).Count -gt 0

    $isLocalAdmin = $null
    if (-not $resolvedBySid)                    { $isLocalAdmin = $null }
    elseif ($directMember -or $tokenHasAdmin)   { $isLocalAdmin = $true }
    elseif ($groupInMembers)                    { $isLocalAdmin = $null }
    else                                        { $isLocalAdmin = $false }

    $locals = @(); $guest = $null
    try {
        foreach ($u in @(Get-CimInstance Win32_UserAccount -Filter "LocalAccount=TRUE" -ErrorAction Stop)) {
            $sid = Prop $u 'SID'
            $locals += [ordered]@{
                name = Prop $u 'Name'; sid = $sid; disabled = Prop $u 'Disabled'
                passwordRequired = Prop $u 'PasswordRequired'
                passwordExpires = Prop $u 'PasswordExpires'
            }
            if ($sid -and $sid -match '-501$') { $guest = (-not (Prop $u 'Disabled')) }
        }
    } catch { }

    [ordered]@{
        administratorsGroupResolvedBySid = $resolvedBySid
        localAdministrators = ArrOrNull $admins
        currentUser = [ordered]@{
            name = $id.Name
            sid = $currentSid
            isLocalAdmin = $isLocalAdmin
            isDomainAccount = if ($cs.PartOfDomain) { $id.Name -like "$($cs.Domain.Split('.')[0])\*" } else { $false }
        }
        localAccounts = ArrOrNull $locals
        guestAccountEnabled = $guest
    }
} -Summary {
    param($d)
    switch ($d.currentUser.isLocalAdmin) {
        $true  { 'Usuário do dia a dia é administrador local' }
        $false { 'Usuário do dia a dia é usuário padrão' }
        default { 'Privilégio do usuário não verificado' }
    }
}

# ---------------------------------------------------------------- 15. bateria

Invoke-Collector -Id 'battery' -DisplayName 'Bateria' -RequiresElevation $false `
    -NotApplicable { @(Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue).Count -eq 0 } -Script {
    # Win32_Battery NÃO entrega capacidade nem ciclos: a sonda confirmou DesignCapacity e
    # FullChargeCapacity nulos e nenhuma propriedade de ciclos. Quem entrega:
    #
    #   root\wmi BatteryCycleCount.CycleCount            -> ciclos
    #   root\wmi BatteryFullChargedCapacity              -> carga plena, em mWh
    #   Win32_PortableBattery.DesignCapacity             -> capacidade de projeto, em mWh
    #
    # As três foram validadas contra powercfg /batteryreport na mesma máquina: ciclos e
    # carga plena bateram EXATAMENTE, projeto ficou a 1 mWh. Nada disto escreve arquivo,
    # que era a única razão de o powercfg estar fora do protótipo.
    #
    # Só o par de classes root\wmi correlaciona por Tag — a sonda mostrou as duas com o
    # mesmo Tag e o mesmo InstanceName. Elas podem exigir elevação (as outras classes de
    # root\wmi exigem); se exigirem, caem no catch e os campos ficam null.
    $rw = @{}
    foreach ($cls in @('BatteryFullChargedCapacity', 'BatteryCycleCount')) {
        try {
            foreach ($i in @(Get-CimInstance -Namespace 'root\wmi' -ClassName $cls -ErrorAction Stop)) {
                $tag = [string](Prop $i 'Tag')
                if (-not $rw.ContainsKey($tag)) { $rw[$tag] = @{ full = $null; cycles = $null } }
                if ($cls -eq 'BatteryFullChargedCapacity') { $rw[$tag].full = Prop $i 'FullChargedCapacity' }
                else { $rw[$tag].cycles = Prop $i 'CycleCount' }
            }
        } catch { }
    }
    $rwTags = @($rw.Keys | Sort-Object)

    $portable = @()
    try { $portable = @(Get-CimInstance Win32_PortableBattery -ErrorAction Stop) } catch { }

    $bats = @(Get-CimInstance Win32_Battery)
    $list = @()
    for ($n = 0; $n -lt $bats.Count; $n++) {
        $b = $bats[$n]

        # Correlação por posição. Com uma bateria — o caso normal — é exata. Com mais de
        # uma, só vale se a fonte trouxer a MESMA quantidade de instâncias; caso contrário
        # fica null em vez de arriscar atribuir o dado de uma bateria à outra.
        $full = $null; $cycles = $null
        if ($rwTags.Count -eq $bats.Count -and $n -lt $rwTags.Count) {
            $full   = $rw[$rwTags[$n]].full
            $cycles = $rw[$rwTags[$n]].cycles
        }
        if ($null -eq $full) { $full = Prop $b 'FullChargeCapacity' }

        $design = Prop $b 'DesignCapacity'
        if ($null -eq $design -and $portable.Count -eq $bats.Count -and $n -lt $portable.Count) {
            # NÃO multiplicar por CapacityMultiplier: DesignCapacity já vem em mWh.
            # Verificado contra o powercfg — multiplicar daria 10x e desgaste negativo.
            $design = Prop $portable[$n] 'DesignCapacity'
        }

        $list += [ordered]@{
            name = Prop $b 'Name'
            # Chemistry = 2 ('Unknown') é o que o hardware realmente reporta em campo.
            # Código fora do mapa vira null, não um palpite.
            chemistry = switch ([int](Prop $b 'Chemistry')) {
                3 { 'Lead Acid' } 4 { 'NiCd' } 5 { 'NiMH' } 6 { 'Li-ion' } 8 { 'LiP' } default { $null } }
            currentChargePercent = Prop $b 'EstimatedChargeRemaining'
            designCapacityMwh = if ($null -ne $design) { [int]$design } else { $null }
            fullChargeCapacityMwh = if ($null -ne $full) { [int]$full } else { $null }
            cycleCount = if ($null -ne $cycles) { [int]$cycles } else { $null }
        }
    }

    $wear = $null; $src = 'unavailable'
    $b0 = @($list)[0]
    if ($b0 -and $b0.designCapacityMwh -and $b0.fullChargeCapacityMwh -and $b0.designCapacityMwh -gt 0) {
        $wear = [math]::Round((1 - ($b0.fullChargeCapacityMwh / $b0.designCapacityMwh)) * 100, 1)
        if ($wear -lt 0) { $wear = 0 }
        $src = 'wmi'
    }
    [ordered]@{ present = ($list.Count -gt 0); batteries = ArrOrNull $list
                wearPercent = $wear; wearSource = $src }
} -Summary {
    param($d)
    # $b0 pode ser null: guardar antes de acessar membro, senão StrictMode 2.0 lança.
    $b0 = @($d.batteries)[0]
    $cyc = if ($b0 -and $null -ne $b0.cycleCount) { ", $($b0.cycleCount) ciclos" } else { '' }
    if ($null -ne $d.wearPercent) { "Desgaste de $($d.wearPercent)%$cyc" }
    else { "Desgaste não pôde ser calculado$cyc" }
}

# ---------------------------------------------------------------- 16. eventos

Invoke-Collector -Id 'events' -DisplayName 'Eventos críticos' -RequiresElevation $false -Script {
    # rules/event-ids.json já tem os IDs levantados na doc oficial (2026-08-03), mas
    # validUntil segue nulo: a validação de campo não foi feita. Enquanto for nulo NÃO
    # avaliamos — EST-001..003 resolvem Indeterminate, que é a degradação segura do
    # ADR-005 aplicada aqui. Ligar a avaliação exige, na ordem: rodar a sonda com filtro
    # de provedor, conferir a contagem contra máquina de histórico conhecido, e corrigir
    # a agregação de EST-003 para recorrência por aplicação.
    # NUNCA coletar o canal Security nem eventos de logon (doc 01 §7.1).
    [ordered]@{
        windowDays = 30
        windowStartedAt = (Get-Date).AddDays(-30).ToString('o')
        evaluated = $false
        reason = 'rules/event-ids.json com IDs levantados na documentação oficial, mas validUntil nulo — validação de campo pendente. Rode Test-DataSources.ps1 numa máquina de histórico conhecido e confira as contagens.'
        unexpectedShutdowns = $null
        diskErrors = $null
        criticalApplicationErrors = $null
        matches = $null
    }
} -Summary { param($d) 'IDs de evento não validados em campo — não avaliado' }

# ============================================================ consolidação

# Campos derivados que dependem de mais de um coletor. Feito aqui, uma vez, em vez
# de acoplar coletores entre si.
Write-Host ''
Write-Host '  Consolidando campos derivados...' -ForegroundColor DarkGray

$av = Get-CollectorData 'antivirus'
$sw = Get-CollectorData 'software'
if ($av) {
    if ($sw) {
        # CRUZAMENTO OBRIGATÓRIO (doc 03 §4.6): impede o pior falso positivo possível —
        # dizer "sem antivírus" para quem tem EDR corporativo que o Security Center não vê.
        # Dedupe manual e atribuicao direta: assim a lista chega ao JSON como array
        # mesmo com um unico item, e vazia vira [] em vez de objeto solto. Um objeto
        # vazio aqui passaria pelo filtro de verdade e viraria um "produto" fantasma
        # nesta lista, que e exatamente a que impede o falso positivo.
        $inv = @()
        foreach ($n in (@($sw.classification.edrAgents) + @($sw.classification.antivirusProducts))) {
            if ($n -is [string] -and $n.Trim() -and $inv -notcontains $n) { $inv += $n }
        }
        $av.securitySoftwareInInventory = $inv
    }
    if ($av.securityCenterAvailable -eq $true) {
        $av.anyProtectionDetected = (@($av.products).Count -gt 0)
    }
}

$sto = Get-CollectorData 'storage'
$mem = Get-CollectorData 'memory'
$cpu = Get-CollectorData 'cpu'
$w11 = Get-CollectorData 'win11'
if ($w11) {
    $r = $w11.requirements
    $r.tpm = if ($w11.tpm.present -eq $false) { 'Fail' }
             elseif ($w11.tpm.present -eq $true -and $null -ne $w11.tpm.majorVersion) {
                 if ($w11.tpm.majorVersion -ge 2 -and $w11.tpm.enabled -ne $false) { 'Pass' } else { 'Fail' }
             } else { 'Unknown' }
    $r.secureBoot = if ($w11.secureBoot.enabled -eq $true) { 'Pass' }
                    elseif ($w11.secureBoot.enabled -eq $false) { 'Fail' } else { 'Unknown' }
    $r.firmware = switch ($w11.firmware.mode) { 'UEFI' { 'Pass' } 'Legacy' { 'Fail' } default { 'Unknown' } }
    $r.ram = if ($mem -and $null -ne $mem.totalGiB) { if ($mem.totalGiB -ge 4) { 'Pass' } else { 'Fail' } } else { 'Unknown' }
    $r.storage = if ($sto -and $sto.systemDisk -and $sto.systemDisk.sizeBytes) {
                     if ($sto.systemDisk.sizeBytes -ge 64GB) { 'Pass' } else { 'Fail' } } else { 'Unknown' }
    # CPU sem a lista oficial embutida = Unknown. Nunca Fail (ADR-006).
    $r.cpu = if ($cpu -and $cpu.win11Supported -eq $true) { 'Pass' }
             elseif ($cpu -and $cpu.win11Supported -eq $false) { 'Fail' } else { 'Unknown' }

    $vals = @($r.GetEnumerator())
    $w11.blockers = @($vals | Where-Object { $_.Value -eq 'Fail' } | ForEach-Object { $_.Key })
    $w11.unknowns = @($vals | Where-Object { $_.Value -eq 'Unknown' } | ForEach-Object { $_.Key })
    # null = nenhum Fail mas há Unknown. Não é "compatível" nem "incompatível".
    $w11.eligible = if ($w11.blockers.Count -gt 0) { $false }
                    elseif ($w11.unknowns.Count -gt 0) { $null } else { $true }
}

# ============================================================ saída

$finishedAt = Get-Date
$machine = Get-CollectorData 'machine'
$hostname = if ($machine -and $machine.hostname) { $machine.hostname } else { $env:COMPUTERNAME }
$serial   = if ($machine -and $machine.productSerial) { $machine.productSerial } else { 'SEM-SERIAL' }

function Get-SafeName {
    param([string] $Value, [string] $Fallback)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $Fallback }
    $clean = ($Value -replace '[\\/:*?"<>|\s]+', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($clean)) { return $Fallback }
    if ($clean.Length -gt 40) { $clean = $clean.Substring(0, 40) }
    return $clean
}

if (-not $OutputPath) {
    $OutputPath = Join-Path (Join-Path $PSScriptRoot 'EpicoraCheckup') (Get-SafeName $Client 'CLIENTE')
}
if (-not (Test-Path $OutputPath)) { $null = New-Item -ItemType Directory -Path $OutputPath -Force }

$baseName = '{0}_{1}_{2}' -f (Get-SafeName $hostname 'HOST'), (Get-SafeName $serial 'SEM-SERIAL'), (Get-Date -Format 'yyyyMMdd')

$document = [ordered]@{
    schemaVersion = $script:SchemaVer
    tool = [ordered]@{
        name = 'EpicoraCheckup'; version = $script:ToolVersion; commit = $null
        runtime = 'powershell'; rulesVersion = $null
    }
    execution = [ordered]@{
        startedAt       = $script:StartedAt.ToString('o')
        finishedAt      = $finishedAt.ToString('o')
        durationSeconds = [int]($finishedAt - $script:StartedAt).TotalSeconds
        elevated        = $script:Elevated
        technician      = $Technician
        diagnosticId    = $DiagnosticId
        hostLocale      = (Get-Culture).Name
    }
    client = [ordered]@{
        name = $Client
        unit = if ([string]::IsNullOrWhiteSpace($Unit)) { $null } else { $Unit }
    }
    manual = [ordered]@{
        machineLabel      = if ([string]::IsNullOrWhiteSpace($MachineLabel)) { $hostname } else { $MachineLabel }
        responsible       = if ([string]::IsNullOrWhiteSpace($Responsible)) { 'não informado' } else { $Responsible }
        department        = if ([string]::IsNullOrWhiteSpace($Department)) { 'não informado' } else { $Department }
        physicalLocation  = if ([string]::IsNullOrWhiteSpace($PhysicalLocation)) { $null } else { $PhysicalLocation }
        assetTag          = if ([string]::IsNullOrWhiteSpace($AssetTag)) { $null } else { $AssetTag }
        physicalCondition = if ([string]::IsNullOrWhiteSpace($PhysicalCondition)) { $null } else { $PhysicalCondition }
        notes             = if ([string]::IsNullOrWhiteSpace($Notes)) { $null } else { $Notes }
        corporateEnvironment = if ($CorporateEnvironment) { $true } else { $null }
    }
    collectors = @($script:Results)
    # O protótipo NÃO avalia regras: quem faz isso é tools/evaluate-rules.mjs no
    # escritório, ou o motor C# na Fase 2. Aqui só produzimos o dado bruto.
    findings = @()
    score = [ordered]@{ value = 100; band = 'Green'; verdict = 'Keep'; verdictDrivenBy = @() }
    optimization = $null
}

$jsonPath = Join-Path $OutputPath "$baseName.json"
$logPath  = Join-Path $OutputPath "$baseName.log"

# UTF-8 SEM BOM. Set-Content -Encoding UTF8 no PowerShell 5.1 escreve COM BOM, e um
# BOM no início do arquivo faz JSON.parse falhar — o consolidador e as ferramentas
# Node não conseguiriam ler a saída. A RFC 8259 também diz que implementações não
# devem acrescentar BOM a JSON.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# -Depth 10 é OBRIGATÓRIO: o padrão do PowerShell 5.1 é 2 e trunca em silêncio.
[IO.File]::WriteAllText($jsonPath, ($document | ConvertTo-Json -Depth 10), $utf8NoBom)
[IO.File]::WriteAllText($logPath, (($script:LogLines -join "`r`n") + "`r`n"), $utf8NoBom)

$ok      = @($script:Results | Where-Object { $_.status -eq 'Completed' }).Count
$skipped = @($script:Results | Where-Object { $_.status -eq 'Skipped' }).Count
$failed  = @($script:Results | Where-Object { $_.status -eq 'Failed' }).Count

Write-Host ''
Write-Host ('Concluído em {0} s' -f $document.execution.durationSeconds) -ForegroundColor Cyan
Write-Host ("{0} coletores concluídos, {1} ignorados, {2} com falha" -f $ok, $skipped, $failed) `
    -ForegroundColor $(if ($failed -gt 0) { 'Yellow' } else { 'Cyan' })
Write-Host ''
Write-Host ('JSON: {0}' -f $jsonPath)
Write-Host ('Log:  {0}' -f $logPath)
Write-Host ''
Write-Host 'Nada foi enviado para nenhum servidor. Copie os arquivos para o Drive da Epicora.' -ForegroundColor DarkGray
Write-Host ''
