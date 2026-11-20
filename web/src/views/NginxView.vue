<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  CircleCheck,
  CircleCheckFilled,
  Connection,
  CopyDocument,
  Document,
  Refresh,
  RefreshRight,
  Search,
  Upload,
  View,
  WarningFilled,
} from '@element-plus/icons-vue'
import { nginxApi } from '../api'
import type { NginxSite, NginxStatus } from '../api/types'
import { errorMessage } from '../api/http'
import { daysLabel, formatDate, issuerName } from '../utils/format'
import StatusTag from '../components/StatusTag.vue'

const router = useRouter()
const sites = ref<NginxSite[]>([])
const status = ref<NginxStatus | null>(null)
const loading = ref(false)
const scanning = ref(false)
const testing = ref(false)
const reloading = ref(false)
const importing = ref(false)
const search = ref('')
const typeFilter = ref<'all' | 'ssl' | 'http'>('all')

const configDialog = ref(false)
const configPath = ref('')
const configContent = ref('')
const configLoading = ref(false)

const filteredSites = computed(() => {
  const term = search.value.trim().toLowerCase()
  return sites.value.filter((site) => {
    if (typeFilter.value === 'ssl' && !site.certificatePath) return false
    if (typeFilter.value === 'http' && site.certificatePath) return false
    if (!term) return true
    return (
      site.serverNames.some((name) => name.toLowerCase().includes(term)) ||
      site.configFile.toLowerCase().includes(term) ||
      (site.certificatePath ?? '').toLowerCase().includes(term)
    )
  })
})

const sslCount = computed(() => sites.value.filter((site) => site.certificatePath).length)
const warning = computed(() => status.value && (!status.value.running || !status.value.configOk))

async function load() {
  loading.value = true
  try {
    const [siteList, nginxStatus] = await Promise.all([nginxApi.sites(), nginxApi.status()])
    sites.value = siteList
    status.value = nginxStatus
  } catch (error) {
    ElMessage.error({ message: errorMessage(error), duration: 8000, showClose: true })
  } finally {
    loading.value = false
  }
}

async function scan() {
  scanning.value = true
  try {
    const result = await nginxApi.scan()
    sites.value = result.sites
    ElMessage.success(`扫描完成：${result.count} 个 server 块，${result.sslCount} 个启用 HTTPS`)
    status.value = await nginxApi.status()
  } catch (error) {
    ElMessage.error({ message: errorMessage(error), duration: 8000, showClose: true })
  } finally {
    scanning.value = false
  }
}

async function testConfig() {
  testing.value = true
  try {
    const result = await nginxApi.test()
    if (result.success) {
      ElMessage.success('nginx 配置检查通过')
    } else {
      ElMessage.error({ message: result.output || 'nginx 配置检查失败', duration: 8000, showClose: true })
    }
    status.value = await nginxApi.status()
  } catch (error) {
    ElMessage.error(errorMessage(error))
  } finally {
    testing.value = false
  }
}

async function reloadNginx() {
  try {
    await ElMessageBox.confirm('将先校验配置，再平滑重载 nginx。是否继续？', '重载 Nginx', {
      confirmButtonText: '重载',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }

  reloading.value = true
  try {
    const result = await nginxApi.reload()
    if (result.success) ElMessage.success('nginx 已重新加载')
    else ElMessage.error(result.output || '重载失败')
    status.value = await nginxApi.status()
  } catch (error) {
    ElMessage.error({ message: errorMessage(error), duration: 8000, showClose: true })
  } finally {
    reloading.value = false
  }
}

async function importCertificates() {
  importing.value = true
  try {
    const result = await nginxApi.importCertificates()
    if (result.imported > 0) {
      ElMessage.success(`已导入 ${result.imported} 张证书：${result.domains.join(', ')}`)
      await load()
    } else {
      ElMessage.info('没有发现新的可导入证书')
    }
  } catch (error) {
    ElMessage.error({ message: errorMessage(error), duration: 8000, showClose: true })
  } finally {
    importing.value = false
  }
}

async function viewConfig(path: string) {
  configDialog.value = true
  configPath.value = path
  configContent.value = ''
  configLoading.value = true
  try {
    const result = await nginxApi.config(path)
    configContent.value = result.content
  } catch (error) {
    ElMessage.error(errorMessage(error))
    configDialog.value = false
  } finally {
    configLoading.value = false
  }
}

function openCertificate(site: NginxSite) {
  if (site.certificateId) {
    router.push({ path: '/certificates', query: { search: site.serverNames[0] ?? '' } })
    return
  }

  if (site.certificatePath) {
    ElMessage.info('该证书尚未纳入管理，可使用“导入证书”加入管理')
  } else {
    ElMessage.info('该站点还没有配置 SSL 证书')
  }
}

async function copy(value: string) {
  try {
    await navigator.clipboard.writeText(value)
    ElMessage.success('已复制')
  } catch {
    ElMessage.warning('复制失败，请手动选择文本')
  }
}

function siteRowKey(row: NginxSite) {
  return row.configFile + row.line
}

function listenTags(site: NginxSite) {
  const tags: { label: string; type: 'success' | 'info' }[] = []
  if (site.listensOn443) tags.push({ label: 'HTTPS', type: 'success' })
  if (site.listensOn80) tags.push({ label: 'HTTP', type: 'info' })
  return tags.length ? tags : [{ label: site.listens.join(' ') || '—', type: 'info' as const }]
}

onMounted(load)
</script>

<template>
  <div class="page">
    <div class="page-heading">
      <div>
        <h1>Nginx 站点</h1>
        <p>自动解析 nginx.conf 及其 include 文件，关联已有证书并支持一键部署</p>
      </div>
      <div class="heading-actions">
        <el-button :icon="Upload" :loading="importing" @click="importCertificates">导入证书</el-button>
        <el-button :icon="Refresh" :loading="scanning" @click="scan">重新扫描</el-button>
        <el-button type="primary" :icon="RefreshRight" :loading="reloading" @click="reloadNginx">重载 Nginx</el-button>
      </div>
    </div>

    <section class="status-strip">
      <div class="status-card">
        <div class="status-icon" :class="{ ok: status?.running, bad: !status?.running }">
          <el-icon><Connection /></el-icon>
        </div>
        <div>
          <span>服务进程</span>
          <strong>{{ status?.running ? '运行中' : '未运行' }}</strong>
        </div>
        <span class="status-dot" :class="{ ok: status?.running }" />
      </div>

      <div class="status-card">
        <div class="status-icon" :class="{ ok: status?.configOk, bad: !status?.configOk }">
          <el-icon><CircleCheckFilled /></el-icon>
        </div>
        <div>
          <span>配置检查</span>
          <strong>{{ status?.configOk ? '通过' : '未通过' }}</strong>
        </div>
        <el-button text size="small" :loading="testing" @click="testConfig">重新检查</el-button>
      </div>

      <div class="status-card">
        <div class="status-icon neutral"><el-icon><Document /></el-icon></div>
        <div>
          <span>Server 块</span>
          <strong>{{ sites.length }}</strong>
        </div>
        <span class="status-meta">{{ sslCount }} 个 HTTPS</span>
      </div>

      <div class="status-card">
        <div class="status-icon neutral"><el-icon><Connection /></el-icon></div>
        <div>
          <span>Nginx 版本</span>
          <strong class="version">{{ (status?.version ?? '—').replace('nginx version: ', '') }}</strong>
        </div>
        <span class="status-meta mono">{{ status?.configPath }}</span>
      </div>
    </section>

    <el-alert v-if="warning" type="error" :closable="false" show-icon class="status-alert">
      <template #title>
        {{ !status?.running ? 'Nginx 当前未运行或无法读取进程状态' : 'Nginx 配置检查未通过' }}
      </template>
      <pre v-if="status?.message" class="alert-pre">{{ status.message }}</pre>
    </el-alert>

    <el-alert type="info" :closable="false" show-icon class="hint-alert">
      <template #title>HTTP-01 验证路径</template>
      启用自动模式后，申请证书时会临时向匹配的 80 端口 server 块注入
      <code>/.well-known/acme-challenge/</code> 验证路径，验证完成立即恢复原配置。也可在设置中切换为仅使用固定 Webroot。
    </el-alert>

    <section class="panel">
      <div class="toolbar">
        <div class="filters">
          <el-input v-model="search" placeholder="搜索域名或配置文件…" clearable style="width: 260px" :prefix-icon="Search" />
          <el-radio-group v-model="typeFilter" size="small">
            <el-radio-button value="all">全部</el-radio-button>
            <el-radio-button value="ssl">HTTPS</el-radio-button>
            <el-radio-button value="http">HTTP</el-radio-button>
          </el-radio-group>
        </div>
        <div class="toolbar-right">
          <el-button :icon="CircleCheck" :loading="testing" text @click="testConfig">检查配置</el-button>
          <el-button :icon="Refresh" :loading="loading" text @click="load">刷新列表</el-button>
        </div>
      </div>

      <el-table v-loading="loading" :data="filteredSites" :row-key="siteRowKey" empty-text="没有匹配的 server 块">
        <el-table-column label="域名" min-width="220">
          <template #default="{ row }">
            <div class="server-cell">
              <div class="server-names">
                <el-tag v-for="name in row.serverNames" :key="name" size="small" effect="plain">
                  {{ name }}
                </el-tag>
                <span v-if="!row.serverNames.length" class="muted">未设置 server_name</span>
              </div>
              <span class="config-location">
                <el-icon><Document /></el-icon>
                {{ row.configFile }}:{{ row.line }}
              </span>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="监听" width="150">
          <template #default="{ row }">
            <div class="listen-tags">
              <el-tag v-for="tag in listenTags(row)" :key="tag.label" size="small" :type="tag.type" effect="light">
                {{ tag.label }}
              </el-tag>
            </div>
            <span class="listen-raw mono">{{ row.listens.join('  ') || '—' }}</span>
          </template>
        </el-table-column>

        <el-table-column label="SSL 证书" min-width="280">
          <template #default="{ row }">
            <div v-if="row.certificatePath" class="cert-cell">
              <div class="cert-path mono" :title="row.certificatePath">{{ row.certificatePath }}</div>
              <div class="cert-meta">
                <StatusTag v-if="row.certificateStatus" :status="row.certificateStatus" />
                <span v-if="row.notAfter">{{ formatDate(row.notAfter, false) }}</span>
                <span v-if="row.daysRemaining !== null && row.daysRemaining !== undefined" class="cert-days">
                  {{ daysLabel(row.daysRemaining) }}
                </span>
              </div>
              <span v-if="row.issuer" class="cert-issuer">{{ issuerName(row.issuer) }}</span>
            </div>
            <div v-else class="no-cert">
              <el-icon><WarningFilled /></el-icon>
              未配置 SSL
            </div>
          </template>
        </el-table-column>

        <el-table-column label="管理状态" width="140">
          <template #default="{ row }">
            <el-tag v-if="row.certificateId" size="small" type="success" effect="light">
              已纳管 #{{ row.certificateId }}
            </el-tag>
            <el-tag v-else-if="row.certificatePath" size="small" type="warning" effect="light">未纳管</el-tag>
            <span v-else class="muted small">—</span>
          </template>
        </el-table-column>

        <el-table-column label="操作" width="200" fixed="right">
          <template #default="{ row }">
            <div class="row-actions">
              <el-button text size="small" :icon="View" @click="viewConfig(row.configFile)">查看配置</el-button>
              <el-button
                v-if="row.certificateId"
                text
                size="small"
                type="primary"
                @click="openCertificate(row)"
              >
                证书详情
              </el-button>
              <el-button v-else-if="row.certificatePath" text size="small" type="primary" @click="importCertificates">
                导入
              </el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </section>

    <el-dialog v-model="configDialog" width="860px" destroy-on-close :title="configPath" class="config-dialog">
      <div v-loading="configLoading" class="config-viewer">
        <div class="config-actions">
          <span class="mono">{{ configPath }}</span>
          <el-button text size="small" :icon="CopyDocument" @click="copy(configContent)">复制</el-button>
        </div>
        <pre class="config-code">{{ configContent }}</pre>
      </div>
    </el-dialog>
  </div>
</template>

<style scoped>
.heading-actions {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}

.status-strip {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 14px;
}

.status-card {
  position: relative;
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px 18px;
  border-radius: 14px;
  border: 1px solid var(--nsm-border);
  background: #fff;
  box-shadow: 0 5px 16px rgba(16, 24, 40, 0.04);
  min-width: 0;
}

.status-icon {
  width: 38px;
  height: 38px;
  border-radius: 11px;
  display: grid;
  place-items: center;
  flex: 0 0 auto;
  font-size: 17px;
  color: #475467;
  background: #f2f4f7;
}

.status-icon.ok {
  color: #027a48;
  background: #ecfdf3;
}

.status-icon.bad {
  color: #b42318;
  background: #fef3f2;
}

.status-icon.neutral {
  color: #2f6fed;
  background: #eaf1ff;
}

.status-card > div:nth-child(2) {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.status-card span {
  font-size: 11px;
  color: var(--nsm-muted);
}

.status-card strong {
  font-size: 14px;
  font-weight: 650;
}

.status-card strong.version {
  font-size: 12.5px;
}

.status-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: #f04438;
  margin-left: auto;
  box-shadow: 0 0 0 4px rgba(240, 68, 56, 0.1);
}

.status-dot.ok {
  background: #12b76a;
  box-shadow: 0 0 0 4px rgba(18, 183, 106, 0.1);
}

.status-meta {
  margin-left: auto;
  font-size: 10.5px !important;
  color: #98a2b3 !important;
  max-width: 130px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.status-alert,
.hint-alert {
  border-radius: 12px;
}

.alert-pre {
  margin: 6px 0 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
  font-size: 11.5px;
  line-height: 1.55;
}

.hint-alert code {
  padding: 1px 4px;
  border-radius: 4px;
  background: rgba(15, 159, 143, 0.1);
  font-family: 'JetBrains Mono', Consolas, monospace;
  font-size: 11.5px;
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  padding: 14px 20px;
  border-bottom: 1px solid var(--nsm-border);
  flex-wrap: wrap;
}

.filters {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.toolbar-right {
  display: flex;
  gap: 4px;
}

.server-cell {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.server-names {
  display: flex;
  flex-wrap: wrap;
  gap: 5px;
}

.config-location {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 10.5px;
  color: #98a2b3;
  font-family: 'JetBrains Mono', Consolas, monospace;
  word-break: break-all;
}

.listen-tags {
  display: flex;
  gap: 5px;
  margin-bottom: 4px;
}

.listen-raw {
  font-size: 10px;
  color: #98a2b3;
  word-break: break-all;
}

.cert-cell {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.cert-path {
  font-size: 11px;
  color: #475467;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.cert-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 11px;
  color: #667085;
}

.cert-days {
  color: #b54708;
}

.cert-issuer {
  font-size: 10.5px;
  color: #98a2b3;
}

.no-cert {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 11.5px;
  color: #b54708;
}

.row-actions {
  display: flex;
  align-items: center;
  gap: 2px;
}

.small {
  font-size: 11px;
}

.config-viewer {
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-height: 180px;
}

.config-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 8px 12px;
  border-radius: 8px;
  background: #f8fafc;
  border: 1px solid #edf1f6;
}

.config-actions span {
  font-size: 11px;
  color: #667085;
  word-break: break-all;
}

.config-code {
  margin: 0;
  max-height: 62vh;
  overflow: auto;
  padding: 16px 18px;
  border-radius: 12px;
  background: #0d1728;
  color: #d6e2f0;
  font-family: 'JetBrains Mono', 'SFMono-Regular', Consolas, monospace;
  font-size: 12px;
  line-height: 1.65;
  white-space: pre-wrap;
  word-break: break-word;
}

@media (max-width: 1200px) {
  .status-strip {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 680px) {
  .status-strip {
    grid-template-columns: 1fr;
  }

  .heading-actions {
    width: 100%;
  }
}
</style>
