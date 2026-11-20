export interface Certificate {
  id: number
  primaryDomain: string
  domains: string[]
  email: string
  challengeType: string
  webRootPath?: string | null
  certPath: string
  keyPath: string
  acmeDirectory?: string | null
  autoRenew: boolean
  autoDeploy: boolean
  source: string
  status: string
  notBefore?: string | null
  notAfter?: string | null
  daysRemaining?: number | null
  subject?: string | null
  issuer?: string | null
  serialNumber?: string | null
  thumbprint?: string | null
  lastError?: string | null
  lastRenewedAt?: string | null
  lastCheckedAt?: string | null
  lastAttemptAt?: string | null
  createdAt: string
  updatedAt: string
  nginxSiteCount: number
}

export interface CertificateOperationResult {
  certificate: Certificate
  message: string
  warning?: string | null
}

export interface DnsChallengeRecord {
  domain: string
  wildcard: boolean
  recordName: string
  recordValue: string
}

export interface DnsChallenge {
  certificateId: number
  orderUrl: string
  expiresAt: string
  records: DnsChallengeRecord[]
}

export interface NginxStatus {
  running: boolean
  configOk: boolean
  message: string
  binary: string
  configPath: string
  version?: string | null
  checkedAt: string
}

export interface NginxSite {
  configFile: string
  line: number
  serverNames: string[]
  listens: string[]
  hasSsl: boolean
  listensOn80: boolean
  listensOn443: boolean
  certificatePath?: string | null
  certificateKeyPath?: string | null
  hasOwnCertificate: boolean
  hasAcmeLocation: boolean
  certificateId?: number | null
  certificateStatus?: string | null
  notAfter?: string | null
  daysRemaining?: number | null
  issuer?: string | null
  error?: string | null
}

export interface ActivityLog {
  id: number
  timestamp: string
  level: string
  category: string
  certificateId?: number | null
  domain?: string | null
  message: string
  details?: string | null
}

export interface StatusBucket {
  status: string
  count: number
}

export interface Dashboard {
  totalCertificates: number
  validCertificates: number
  expiringSoon: number
  expiredCertificates: number
  errorCertificates: number
  autoRenewEnabled: number
  nginxSites: number
  nginxSslSites: number
  renewedLast30Days: number
  nextExpiry?: string | null
  nextExpiryDomain?: string | null
  nginx: NginxStatus
  upcoming: Certificate[]
  recentActivity: ActivityLog[]
  statusBuckets: StatusBucket[]
}

export interface AppSettings {
  acmeDirectory: string
  acmeStagingDirectory: string
  defaultEmail: string
  renewDaysBefore: number
  checkIntervalMinutes: number
  autoRenewEnabled: boolean
  nginxBinary: string
  nginxConfigPath: string
  nginxPrefix: string
  nginxReloadCommand: string
  nginxTestEnabled: boolean
  defaultWebRoot: string
  certificateRoot: string
  challengeMode: string
  autoImport: boolean
  webhookUrl: string
  adminUsername: string
  isDefaultPassword: boolean
  letsEncryptProduction: string
  letsEncryptStaging: string
}

export interface LoginResponse {
  token: string
  username: string
  expiresAt: string
  isDefaultPassword: boolean
}

export interface ImportResult {
  imported: number
  skipped: number
  domains: string[]
}

export interface DeployResult {
  configUpdated: boolean
  reloaded: boolean
  updatedFiles: string[]
  message: string
  output?: string | null
}

export interface PagedLogs {
  items: ActivityLog[]
  total: number
  page: number
  pageSize: number
}
