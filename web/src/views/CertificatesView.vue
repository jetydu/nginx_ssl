<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useRoute } from 'vue-router'
import { ElLoading, ElMessage, ElMessageBox } from 'element-plus'
import type { FormInstance, FormRules } from 'element-plus'
import {
  Connection,
  CopyDocument,
  Delete,
  Document,
  Edit,
  MoreFilled,
  Plus,
  Refresh,
  RefreshRight,
  Upload,
  View,
} from '@element-plus/icons-vue'
import { certificateApi, nginxApi, settingsApi } from '../api'
import type { AppSettings, Certificate, DnsChallenge, NginxSite } from '../api/types'
import { errorMessage } from '../api/http'
import { daysLabel, formatDate, issuerName, relativeTime } from '../utils/format'
import StatusTag from '../components/StatusTag.vue'

const route = useRoute()
const certificates = ref<Certificate[]>([])
const loading = ref(false)
const search = ref(typeof route.query.search === 'string' ? route.query.search : '')
const statusFilter = ref('all')
const autoRenewFilter = ref('all')
const settings = ref<AppSettings | null>(null)

const renewingId = ref<number | null>(null)
const syncingId = ref<number | null>(null)
const deployingId = ref<number | null>(null)
const importing = ref(false)

const dialogVisible = ref(false)
const editingId = ref<number | null>(null)
const submitting = ref(false)
const formRef = ref<FormInstance>()

const drawerVisible = ref(false)
const activeCert = ref<Certificate | null>(null)
const detailSites = ref<NginxSite[]>([])
const detailLoading = ref(false)

const dnsDialogVisible = ref(false)
const dnsChallenge = ref<DnsChallenge | null>(null)
const dnsCert = ref<Certificate | null>(null)
const dnsSubmitting = ref(false)

const acmeCustomPlaceholder = 'https://acme.example.com/directory'

const form = reactive({
  domainsText: '',
  email: '',
  acmeEnv: 'production' as 'production' | 'staging' | 'custom',
  acmeDirectory: '',
  webRootPath: '',
  certPath: '',
  keyPath: '',
  autoRenew: true,
  autoDeploy: true,
  issueNow: true,
})

const rules: FormRules = {
  domainsText: [
    { required: true, message: '请填写至少一个域名', trigger: 'blur' },
    {
      validator: (_rule, value: string, callback) => {
        const list = parseDomains(value)
        if (!list.length) {
          callback(new Error('请填写至少一个域名'))
          return
        }
        const invalid = list.find(
          (item) =>
            !/^(\*\.)?[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$/i.test(
              item,
            ),
        )
        if (invalid) {
          callback(new Error(`域名格式不正确：${invalid}`))
          return
        }
        callback()
      },
      trigger: 'blur',
    },
  ],
}

function parseDomains(value: string): string[] {
  return Array.from(
    new Set(
      value
        .split(/[\s,;]+/)
        .map((item) => item.trim().toLowerCase())
        .filter(Boolean),
    ),
  )
}

const wildcardInForm = computed(() =>
  parseDomains(form.domainsText).some((item) => item.startsWith('*.')),
)

const challengeLabel = computed(() =>
  wildcardInForm.value
    ? 'DNS-01（通配符需手动添加 TXT 记录）'
    : 'HTTP-01（自动配置 Nginx 验证路径）',
)

function certNeedsDns(cert: Certificate): boolean {
  return cert.challengeType === 'dns-01' || cert.domains.some((item) => item.startsWith('*.'))
}

async function load() {
  loading.value = true
  try {
    certificates.value = await certificateApi.list({
      search: search.value.trim() || undefined,
      status: statusFilter.value === 'all' ? undefined : statusFilter.value,
      autoRenew:
        autoRenewFilter.value === 'all' ? undefined : autoRenewFilter.value === 'yes',
    })
  } catch (error) {
    ElMessage.error(errorMessage(error))
  } finally {
    loading.value = false
  }
}

async function loadSettings() {
  try {
    settings.value = await settingsApi.get()
  } catch {
    // Settings are only used for placeholders here.
  }
}

function resetForm() {
  form.domainsText = ''
  form.email = settings.value?.defaultEmail ?? ''
  form.acmeEnv = 'production'
  form.acmeDirectory = ''
  form.webRootPath = ''
  form.certPath = ''
  form.keyPath = ''
  form.autoRenew = true
  form.autoDeploy = true
  form.issueNow = true
}

function openCreate() {
  editingId.value = null
  resetForm()
  dialogVisible.value = true
  setTimeout(() => formRef.value?.clearValidate(), 0)
}

function openEdit(row: Certificate) {
  editingId.value = row.id
  form.domainsText = row.domains.join('\n')
  form.email = row.email ?? ''
  form.acmeEnv = row.acmeDirectory
    ? row.acmeDirectory === settings.value?.acmeStagingDirectory
      ? 'staging'
      : 'custom'
    : 'production'
  form.acmeDirectory = form.acmeEnv === 'custom' ? (row.acmeDirectory ?? '') : ''
  form.webRootPath = row.webRootPath ?? ''
  form.certPath = row.certPath ?? ''
  form.keyPath = row.keyPath ?? ''
  form.autoRenew = row.autoRenew
  form.autoDeploy = row.autoDeploy
  form.issueNow = false
  dialogVisible.value = true
  setTimeout(() => formRef.value?.clearValidate(), 0)
}

function buildPayload() {
  const acmeDirectory =
    form.acmeEnv === 'staging'
      ? settings.value?.acmeStagingDirectory ?? 'https://acme-staging-v02.api.letsencrypt.org/directory'
      : form.acmeEnv === 'custom'
        ? form.acmeDirectory.trim()
        : null

  return {
    domains: parseDomains(form.domainsText),
    email: form.email.trim(),
    challengeType: wildcardInForm.value ? 'dns-01' : 'http-01',
    webRootPath: form.webRootPath.trim() || null,
    certPath: form.certPath.trim() || null,
    keyPath: form.keyPath.trim() || null,
    acmeDirectory: acmeDirectory,
    autoRenew: wildcardInForm.value ? false : form.autoRenew,
    autoDeploy: form.autoDeploy,
  }
}

async function submitForm() {
  const valid = await formRef.value?.validate().catch(() => false)
  if (!valid) return

  if (form.acmeEnv === 'custom' && !/^https:\/\//i.test(form.acmeDirectory.trim())) {
    ElMessage.warning('自定义 ACME 目录必须是 HTTPS 地址')
    return
  }

  submitting.value = true
  let created: Certificate | null = null
  try {
    const payload = buildPayload()
    if (editingId.value) {
      await certificateApi.update(editingId.value, payload)
      ElMessage.success('证书配置已更新')
    } else {
      created = await certificateApi.create(payload)
      ElMessage.success('证书配置已创建')
    }

    dialogVisible.value = false
    await load()

    if (created && form.issueNow) {
      await issueNewCertificate(created)
    }
  } catch (error) {
    ElMessage.error({ message: errorMessage(error), duration: 8000, showClose: true })
  } finally {
    submitting.value = false
  }
}

async function issueNewCertificate(cert: Certificate) {
  if (certNeedsDns(cert)) {
    await startDnsChallenge(cert)
    return
  }

  const instance = ElLoading.service({
    lock: true,
    text: `正在为 ${cert.primaryDomain} 申请证书，HTTP-01 验证通常需要 10-60 秒…`,
    background: 'rgba(255, 255, 255, 0.72)',
  })

  try {
    const result = await certificateApi.renew(cert.id, true)
    if (result.warning) {
      ElMessage.warning({ message: result.warning, duration: 8000, showClose: true })
    } else {
      ElMessage.success(`${result.certificate.primaryDomain} 证书签发成功`)
    }
  } catch (error) {
    ElMessage.error({ message: errorMessage(error), duration: 10000, showClose: true })
  } finally {
    instance.close()
    await load()
  }
}

async function startDnsChallenge(cert: Certificate) {
  const instance = ElLoading.service({
    lock: true,
    text: `正在为 ${cert.primaryDomain} 创建 DNS-01 订单…`,
    background: 'rgba(255, 255, 255, 0.72)',
  })

  try {
    dnsCert.value = cert
    dnsChallenge.value = await certificateApi.prepareDnsChallenge(cert.id)
    dnsDialogVisible.value = true
  } catch (error) {
    dnsCert.value = null
    dnsChallenge.value = null
    ElMessage.error({ message: errorMessage(error), duration: 10000, showClose: true })
  } finally {
    instance.close()
    await load()
  }
}

async function completeDnsChallenge() {
  if (!dnsCert.value) return

  dnsSubmitting.value = true
  try {
    const result = await certificateApi.completeDnsChallenge(dnsCert.value.id)
    if (result.warning) {
      ElMessage.warning({ message: result.warning, duration: 9000, showClose: true })
    } else {
      ElMessage.success(`${result.certificate.primaryDomain} 证书签发成功`)
    }
    dnsDialogVisible.value = false
    dnsChallenge.value = null
    dnsCert.value = null
    await load()
    if (drawerVisible.value) await refreshDrawer()
  } catch (error) {
    ElMessage.error({ message: errorMessage(error), duration: 12000, showClose: true })
    await load()
  } finally {
    dnsSubmitting.value = false
  }
}

function closeDnsDialog() {
  dnsDialogVisible.value = false
}

async function renew(row: Certificate) {
  const dns = certNeedsDns(row)
  try {
    await ElMessageBox.confirm(
      dns
        ? `通配符证书需要通过 DNS-01 手动验证：系统会生成 ${row.primaryDomain} 的 _acme-challenge TXT 记录，你到 DNS 服务商添加后回来点“继续验证”即可。是否现在生成订单？`
        : `将立即通过 Let's Encrypt 重新申请 ${row.primaryDomain} 的证书，并自动更新 nginx。是否继续？`,
      dns ? 'DNS 验证续期' : '立即续期',
      {
        confirmButtonText: dns ? '生成 TXT 记录' : '开始续期',
        cancelButtonText: '取消',
        type: 'info',
      },
    )
  } catch {
    return
  }

  if (dns) {
    await startDnsChallenge(row)
    return
  }

  renewingId.value = row.id
  const instance = ElLoading.service({
    lock: true,
    text: `正在续期 ${row.primaryDomain}，请稍候…`,
    background: 'rgba(255, 255, 255, 0.7)',
  })

  try {
    const result = await certificateApi.renew(row.id, true)
    if (result.warning) {
      ElMessage.warning({ message: result.warning, duration: 9000, showClose: true })
    } else {
      ElMessage.success(result.message || '续期成功')
    }
    await load()
    if (drawerVisible.value) await refreshDrawer()
  } catch (error) {
    ElMessage.error({ message: errorMessage(error), duration: 10000, showClose: true })
  } finally {
    instance.close()
    renewingId.value = null
  }
}

async function sync(row: Certificate) {
  syncingId.value = row.id
  try {
    await certificateApi.sync(row.id)
    ElMessage.success('已重新读取证书信息')
    await load()
    if (drawerVisible.value) await refreshDrawer()
  } catch (error) {
    ElMessage.error(errorMessage(error))
  } finally {
    syncingId.value = null
  }
}

async function deploy(row: Certificate) {
  try {
    await ElMessageBox.confirm(
      `将把证书路径写入匹配的 nginx server 块，检查配置并平滑重载 nginx。是否继续？`,
      '部署到 Nginx',
      { confirmButtonText: '部署', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  deployingId.value = row.id
  try {
    const result = await certificateApi.deploy(row.id)
    ElMessage.success(result.message + (result.reloaded ? '，nginx 已重载' : ''))
    await load()
  } catch (error) {
    ElMessage.error({ message: errorMessage(error), duration: 8000, showClose: true })
  } finally {
    deployingId.value = null
  }
}

async function toggleAutoRenew(row: Certificate, value: boolean) {
  const previous = row.autoRenew
  row.autoRenew = value
  try {
    await certificateApi.update(row.id, { autoRenew: value })
    ElMessage.success(value ? '已开启自动续期' : '已关闭自动续期')
  } catch (error) {
    row.autoRenew = previous
    ElMessage.error(errorMessage(error))
  }
}

async function remove(row: Certificate) {
  try {
    await ElMessageBox.confirm(
      `确定删除 ${row.primaryDomain} 的管理记录吗？磁盘上的证书文件和 nginx 配置不会被删除。`,
      '删除证书记录',
      { confirmButtonText: '删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }

  try {
    await certificateApi.remove(row.id)
    ElMessage.success('记录已删除')
    await load()
  } catch (error) {
    ElMessage.error(errorMessage(error))
  }
}

async function importCertificates() {
  importing.value = true
  try {
    const result = await nginxApi.importCertificates()
    if (result.imported > 0) {
      ElMessage.success(`已从 nginx 导入 ${result.imported} 张证书`)
    } else {
      ElMessage.info('没有发现新的可导入证书')
    }
    await load()
  } catch (error) {
    ElMessage.error(errorMessage(error))
  } finally {
    importing.value = false
  }
}

async function openDrawer(row: Certificate) {
  activeCert.value = row
  drawerVisible.value = true
  detailLoading.value = true
  try {
    detailSites.value = await nginxApi.certificateSites(row.id)
  } catch {
    detailSites.value = []
  } finally {
    detailLoading.value = false
  }
}

async function refreshDrawer() {
  if (!activeCert.value) return
  try {
    const fresh = await certificateApi.get(activeCert.value.id)
    activeCert.value = fresh
    detailSites.value = await nginxApi.certificateSites(fresh.id)
  } catch (error) {
    ElMessage.error(errorMessage(error))
  }
}

function handleCommand(command: string, row: Certificate) {
  if (command === 'sync') sync(row)
  if (command === 'deploy') deploy(row)
  if (command === 'edit') openEdit(row)
  if (command === 'delete') remove(row)
  if (command === 'view') openDrawer(row)
}

async function copy(value: string) {
  try {
    await navigator.clipboard.writeText(value)
    ElMessage.success('已复制')
  } catch {
    ElMessage.warning('复制失败，请手动选择文本')
  }
}

onMounted(async () => {
  await Promise.all([load(), loadSettings()])
})
</script>

<template>
  <div class="page">
    <div class="page-heading">
      <div>
        <h1>证书管理</h1>
        <p>集中申请、监控与续期 Let's Encrypt 证书，并同步到 nginx 配置</p>
      </div>
      <div class="heading-actions">
        <el-button :icon="Upload" :loading="importing" @click="importCertificates">从 Nginx 导入</el-button>
        <el-button type="primary" :icon="Plus" @click="openCreate">新建证书</el-button>
      </div>
    </div>

    <section class="panel">
      <div class="toolbar">
        <div class="filters">
          <el-input
            v-model="search"
            placeholder="搜索域名…"
            clearable
            style="width: 240px"
            :prefix-icon="Document"
            @keyup.enter="load"
            @clear="load"
          />
          <el-select v-model="statusFilter" style="width: 140px" @change="load">
            <el-option label="全部状态" value="all" />
            <el-option label="有效" value="healthy" />
            <el-option label="即将到期" value="soon" />
            <el-option label="已过期" value="expired" />
            <el-option label="异常" value="error" />
            <el-option label="待签发" value="pending" />
          </el-select>
          <el-select v-model="autoRenewFilter" style="width: 140px" @change="load">
            <el-option label="全部续期" value="all" />
            <el-option label="自动续期开" value="yes" />
            <el-option label="自动续期关" value="no" />
          </el-select>
          <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        </div>
        <span class="count-hint">共 {{ certificates.length }} 张证书</span>
      </div>

      <el-table
        v-loading="loading"
        :data="certificates"
        row-key="id"
        class="cert-table"
        empty-text="暂无证书，点击右上角创建或从 nginx 导入"
      >
        <el-table-column label="域名" min-width="240">
          <template #default="{ row }">
            <div class="domain-cell" @click="openDrawer(row)">
              <span class="domain-primary">{{ row.primaryDomain }}</span>
              <div class="domain-sub">
                <el-tag v-if="row.domains.length > 1" size="small" effect="plain" type="info">
                  {{ row.domains.length }} 个域名
                </el-tag>
                <span class="source-tag">{{ row.source === 'imported' ? 'Nginx 导入' : row.source === 'letsencrypt' ? "Let's Encrypt" : '手动配置' }}</span>
              </div>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="状态" width="120">
          <template #default="{ row }">
            <StatusTag :status="row.status" />
          </template>
        </el-table-column>

        <el-table-column label="到期时间" width="190">
          <template #default="{ row }">
            <div class="expiry-cell">
              <span>{{ formatDate(row.notAfter) }}</span>
              <span
                class="expiry-days"
                :class="{ danger: (row.daysRemaining ?? 999) < 7, warning: (row.daysRemaining ?? 999) < 30 }"
              >
                {{ daysLabel(row.daysRemaining) }}
              </span>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="签发机构" min-width="160">
          <template #default="{ row }">
            <div class="issuer-cell">
              <span>{{ issuerName(row.issuer) }}</span>
              <span class="muted">最后检查 {{ relativeTime(row.lastCheckedAt) }}</span>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="自动续期" width="110" align="center">
          <template #default="{ row }">
            <el-switch
              :model-value="row.autoRenew"
              size="small"
              @change="toggleAutoRenew(row, Boolean($event))"
            />
          </template>
        </el-table-column>

        <el-table-column label="Nginx" width="110" align="center">
          <template #default="{ row }">
            <el-tag v-if="row.nginxSiteCount" size="small" effect="light" type="success">
              {{ row.nginxSiteCount }} 个站点
            </el-tag>
            <span v-else class="muted small">未关联</span>
          </template>
        </el-table-column>

        <el-table-column label="操作" width="190" fixed="right">
          <template #default="{ row }">
            <div class="row-actions">
              <el-button
                text
                type="primary"
                size="small"
                :loading="renewingId === row.id"
                @click="renew(row)"
              >
                续期
              </el-button>
              <el-button text size="small" :loading="syncingId === row.id" @click="sync(row)">同步</el-button>
              <el-dropdown trigger="click" @command="handleCommand($event, row)">
                <el-button text size="small" :icon="MoreFilled" />
                <template #dropdown>
                  <el-dropdown-menu>
                    <el-dropdown-item command="view" :icon="View">查看详情</el-dropdown-item>
                    <el-dropdown-item command="deploy" :icon="Connection" :disabled="deployingId === row.id">
                      部署到 Nginx
                    </el-dropdown-item>
                    <el-dropdown-item command="edit" :icon="Edit">编辑配置</el-dropdown-item>
                    <el-dropdown-item command="delete" :icon="Delete" divided>删除记录</el-dropdown-item>
                  </el-dropdown-menu>
                </template>
              </el-dropdown>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </section>

    <el-dialog
      v-model="dialogVisible"
      :title="editingId ? '编辑证书配置' : '新建证书'"
      width="92%"
      top="4vh"
      class="cert-dialog"
      destroy-on-close
      :close-on-click-modal="false"
    >
      <el-form ref="formRef" :model="form" :rules="rules" label-position="top">
        <el-form-item label="域名" prop="domainsText">
          <el-input
            v-model="form.domainsText"
            type="textarea"
            :rows="3"
            placeholder="example.com&#10;www.example.com&#10;*.example.com"
          />
          <div class="field-hint">
            每行一个域名，也支持空格或逗号分隔。第一个域名将作为主域名和证书文件名。通配符域名（如
            *.example.com）会自动使用 DNS-01 验证。
          </div>
        </el-form-item>

        <div class="form-row">
          <el-form-item label="联系邮箱">
            <el-input v-model="form.email" placeholder="admin@example.com" />
          </el-form-item>
          <el-form-item label="验证方式">
            <el-input :model-value="challengeLabel" disabled />
          </el-form-item>
        </div>

        <div class="switch-row">
          <div class="switch-item">
            <div>
              <strong>自动续期</strong>
              <span v-if="wildcardInForm">
                通配符证书需要手动 DNS 验证，自动续期已禁用
              </span>
              <span v-else>到期前 {{ settings?.renewDaysBefore ?? 30 }} 天自动重新签发</span>
            </div>
            <el-switch v-model="form.autoRenew" :disabled="wildcardInForm" />
          </div>
          <div class="switch-item">
            <div>
              <strong>自动部署</strong>
              <span>签发后更新 nginx 配置并平滑重载</span>
            </div>
            <el-switch v-model="form.autoDeploy" />
          </div>
        </div>

        <el-collapse class="advanced">
          <el-collapse-item title="高级设置" name="advanced">
            <div class="form-row">
              <el-form-item label="ACME 环境">
                <el-select v-model="form.acmeEnv" style="width: 100%">
                  <el-option label="Let's Encrypt 生产环境" value="production" />
                  <el-option label="Let's Encrypt 测试环境（Staging）" value="staging" />
                  <el-option label="自定义 ACME 目录" value="custom" />
                </el-select>
              </el-form-item>
              <el-form-item v-if="form.acmeEnv === 'custom'" label="自定义目录地址">
                <el-input v-model="form.acmeDirectory" :placeholder="acmeCustomPlaceholder" />
              </el-form-item>
              <el-form-item v-else label="证书存放目录">
                <el-input :model-value="settings?.certificateRoot ?? '/etc/nginx/ssl/letsencrypt'" disabled />
              </el-form-item>
            </div>

            <el-form-item label="Webroot 目录">
              <el-input v-model="form.webRootPath" :placeholder="settings?.defaultWebRoot ?? '/var/www/letsencrypt'" />
              <div class="field-hint">ACME 验证文件会写入该目录（若启用自动注入验证路径）。</div>
            </el-form-item>

            <div class="form-row">
              <el-form-item label="证书文件路径">
                <el-input v-model="form.certPath" placeholder="留空自动生成 fullchain.pem" />
              </el-form-item>
              <el-form-item label="私钥文件路径">
                <el-input v-model="form.keyPath" placeholder="留空自动生成 privkey.pem" />
              </el-form-item>
            </div>
            <div class="field-hint">修改已有证书路径后，点击“部署到 Nginx”可将新路径写入 server 块。</div>
          </el-collapse-item>
        </el-collapse>

        <div v-if="!editingId" class="issue-now">
          <el-checkbox v-model="form.issueNow">创建后立即申请证书</el-checkbox>
          <span v-if="wildcardInForm">
            将生成 DNS TXT 记录，需要到域名服务商添加后继续验证。
          </span>
          <span v-else>需要域名已解析到本机，且 80 端口可从公网访问。</span>
        </div>
      </el-form>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="submitForm">
          {{ editingId ? '保存修改' : form.issueNow ? '创建并申请' : '仅创建配置' }}
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="dnsDialogVisible"
      title="DNS 验证"
      width="680px"
      top="4vh"
      destroy-on-close
      :close-on-click-modal="false"
      @closed="closeDnsDialog"
    >
      <el-alert type="warning" show-icon :closable="false" class="dns-alert">
        请到域名服务商添加以下 TXT 记录，等待 DNS 生效（通常几分钟）后点击“继续验证”。
        同名记录需要添加多条 TXT 值。
      </el-alert>

      <div v-if="dnsChallenge" class="dns-record-list">
        <div v-for="record in dnsChallenge.records" :key="record.recordName + record.recordValue" class="dns-record">
          <div class="dns-record-head">
            <strong>{{ record.wildcard ? `*.${record.domain}` : record.domain }}</strong>
          </div>
          <div class="dns-kv">
            <span>记录名</span>
            <code>{{ record.recordName }}</code>
            <el-button text size="small" @click="copy(record.recordName)">复制</el-button>
          </div>
          <div class="dns-kv">
            <span>记录类型</span>
            <code>TXT</code>
          </div>
          <div class="dns-kv">
            <span>记录值</span>
            <code>{{ record.recordValue }}</code>
            <el-button text size="small" @click="copy(record.recordValue)">复制</el-button>
          </div>
        </div>
      </div>

      <template #footer>
        <el-button @click="dnsDialogVisible = false">稍后继续</el-button>
        <el-button type="primary" :loading="dnsSubmitting" @click="completeDnsChallenge">
          我已添加，继续验证
        </el-button>
      </template>
    </el-dialog>

    <el-drawer v-model="drawerVisible" size="520px" :with-header="false">
      <div v-if="activeCert" v-loading="detailLoading" class="detail">
        <div class="detail-head">
          <div>
            <span class="detail-kicker">证书详情</span>
            <h2>{{ activeCert.primaryDomain }}</h2>
          </div>
          <StatusTag :status="activeCert.status" size="default" />
        </div>

        <el-alert
          v-if="activeCert.lastError"
          type="error"
          :closable="false"
          show-icon
          title="最近一次操作出现问题"
          class="detail-alert"
        >
          {{ activeCert.lastError }}
        </el-alert>

        <div class="detail-actions">
          <el-button type="primary" :icon="RefreshRight" :loading="renewingId === activeCert.id" @click="renew(activeCert)">
            立即续期
          </el-button>
          <el-button :icon="Refresh" :loading="syncingId === activeCert.id" @click="sync(activeCert)">同步状态</el-button>
          <el-button :icon="Connection" :loading="deployingId === activeCert.id" @click="deploy(activeCert)">部署</el-button>
        </div>

        <div class="detail-section">
          <h3>域名</h3>
          <div class="tag-wrap">
            <el-tag v-for="domain in activeCert.domains" :key="domain" effect="plain">{{ domain }}</el-tag>
          </div>
        </div>

        <div class="detail-section">
          <h3>有效期</h3>
          <div class="detail-grid">
            <div><span>生效时间</span><b>{{ formatDate(activeCert.notBefore) }}</b></div>
            <div><span>到期时间</span><b>{{ formatDate(activeCert.notAfter) }}</b></div>
            <div><span>剩余天数</span><b>{{ daysLabel(activeCert.daysRemaining) }}</b></div>
            <div><span>签发机构</span><b>{{ issuerName(activeCert.issuer) }}</b></div>
          </div>
        </div>

        <div class="detail-section">
          <h3>签发配置</h3>
          <div class="detail-grid">
            <div><span>来源</span><b>{{ activeCert.source }}</b></div>
            <div><span>联系邮箱</span><b>{{ activeCert.email || '未设置' }}</b></div>
            <div><span>验证方式</span><b>{{ activeCert.challengeType }}</b></div>
            <div><span>自动续期</span><b>{{ activeCert.autoRenew ? '已开启' : '已关闭' }}</b></div>
          </div>
        </div>

        <div class="detail-section">
          <h3>文件路径</h3>
          <div class="path-row">
            <span>证书</span>
            <code>{{ activeCert.certPath }}</code>
            <el-button text size="small" :icon="CopyDocument" @click="copy(activeCert.certPath)" />
          </div>
          <div class="path-row">
            <span>私钥</span>
            <code>{{ activeCert.keyPath }}</code>
            <el-button text size="small" :icon="CopyDocument" @click="copy(activeCert.keyPath)" />
          </div>
        </div>

        <div class="detail-section">
          <h3>关联的 Nginx 站点</h3>
          <div v-if="detailSites.length" class="site-list">
            <div v-for="site in detailSites" :key="site.configFile + site.line" class="site-item">
              <div>
                <strong>{{ site.serverNames.join(', ') }}</strong>
                <span>{{ site.configFile }}:{{ site.line }}</span>
              </div>
              <el-tag size="small" effect="plain">{{ site.listens.join(' / ') }}</el-tag>
            </div>
          </div>
          <div v-else class="empty-inline">还没有 nginx 站点使用该证书</div>
        </div>

        <div class="detail-section">
          <h3>时间线</h3>
          <div class="detail-grid">
            <div><span>创建</span><b>{{ formatDate(activeCert.createdAt) }}</b></div>
            <div><span>最后检查</span><b>{{ formatDate(activeCert.lastCheckedAt) }}</b></div>
            <div><span>最后续期</span><b>{{ formatDate(activeCert.lastRenewedAt) }}</b></div>
            <div><span>最后尝试</span><b>{{ formatDate(activeCert.lastAttemptAt) }}</b></div>
          </div>
        </div>

        <div v-if="activeCert.thumbprint" class="fingerprint">
          <span>SHA-1 指纹</span>
          <code>{{ activeCert.thumbprint }}</code>
        </div>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
/* 新建/编辑证书弹窗：接近全屏，内容区独立滚动，底部按钮固定可见 */
:global(.cert-dialog) {
  max-width: 1080px;
  max-height: 92vh;
  margin-bottom: 4vh;
  display: flex;
  flex-direction: column;
}

:global(.cert-dialog .el-dialog__header) {
  flex: 0 0 auto;
}

:global(.cert-dialog .el-dialog__body) {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
}

:global(.cert-dialog .el-dialog__footer) {
  flex: 0 0 auto;
  border-top: 1px solid var(--nsm-border);
}

.dns-alert {
  margin-bottom: 14px;
}

.dns-record-list {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.dns-record {
  border: 1px solid var(--nsm-border);
  border-radius: 10px;
  padding: 12px 14px;
}

.dns-record-head {
  margin-bottom: 10px;
}

.dns-kv {
  display: grid;
  grid-template-columns: 72px minmax(0, 1fr) auto;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}

.dns-kv code {
  background: var(--nsm-surface-soft);
  border-radius: 6px;
  padding: 4px 8px;
  word-break: break-all;
  font-size: 12px;
}
@media (max-width: 760px) {
  :global(.cert-dialog) {
    width: 100% !important;
    max-width: none;
    height: 100vh;
    max-height: 100vh;
    margin: 0;
    border-radius: 0;
  }
}

.heading-actions {
  display: flex;
  gap: 10px;
}

.toolbar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  padding: 16px 20px;
  border-bottom: 1px solid var(--nsm-border);
  flex-wrap: wrap;
}

.filters {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.count-hint {
  font-size: 12px;
  color: var(--nsm-muted);
}

.domain-cell {
  cursor: pointer;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.domain-primary {
  font-weight: 600;
  color: var(--nsm-text);
  font-size: 13.5px;
}

.domain-primary:hover {
  color: var(--nsm-brand-dark);
}

.domain-sub {
  display: flex;
  align-items: center;
  gap: 8px;
}

.source-tag {
  font-size: 11px;
  color: #98a2b3;
}

.expiry-cell {
  display: flex;
  flex-direction: column;
  gap: 3px;
  font-size: 12.5px;
}

.expiry-days {
  font-size: 11.5px;
  color: #667085;
}

.expiry-days.warning {
  color: #b54708;
}

.expiry-days.danger {
  color: #b42318;
  font-weight: 600;
}

.issuer-cell {
  display: flex;
  flex-direction: column;
  gap: 3px;
  font-size: 12.5px;
}

.issuer-cell .muted {
  font-size: 11px;
}

.small {
  font-size: 11px;
}

.row-actions {
  display: flex;
  align-items: center;
  gap: 2px;
}

.field-hint {
  width: 100%;
  margin-top: 6px;
  font-size: 11.5px;
  line-height: 1.5;
  color: #98a2b3;
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 14px;
}

.switch-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  margin-bottom: 14px;
}

.switch-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 12px 14px;
  border: 1px solid var(--nsm-border);
  border-radius: 10px;
  background: #fbfcfe;
}

.switch-item div {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.switch-item strong {
  font-size: 13px;
  font-weight: 600;
}

.switch-item span {
  font-size: 11px;
  color: var(--nsm-muted);
}

.advanced {
  border-top: 1px solid var(--nsm-border);
  border-bottom: none;
  margin-top: 4px;
}

.advanced :deep(.el-collapse-item__header) {
  font-size: 13px;
  font-weight: 500;
  color: #475467;
}

.issue-now {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-top: 14px;
  padding: 12px 14px;
  border-radius: 10px;
  background: #f0f9f7;
  border: 1px solid #d5efeb;
}

.issue-now span {
  font-size: 11.5px;
  color: #6b8f8a;
  padding-left: 24px;
}

.detail {
  display: flex;
  flex-direction: column;
  gap: 22px;
}

.detail-head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
}

.detail-kicker {
  font-size: 11px;
  letter-spacing: 0.12em;
  color: #98a2b3;
  text-transform: uppercase;
}

.detail-head h2 {
  margin: 4px 0 0;
  font-size: 22px;
  letter-spacing: -0.02em;
  word-break: break-all;
}

.detail-alert {
  margin-top: -6px;
}

.detail-actions {
  display: flex;
  gap: 9px;
  flex-wrap: wrap;
}

.detail-section h3 {
  margin: 0 0 10px;
  font-size: 12px;
  font-weight: 600;
  color: #667085;
  letter-spacing: 0.04em;
}

.tag-wrap {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.detail-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px 18px;
}

.detail-grid > div {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.detail-grid span {
  font-size: 11px;
  color: #98a2b3;
}

.detail-grid b {
  font-size: 12.5px;
  font-weight: 550;
  color: #344054;
  word-break: break-word;
}

.path-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 9px 0;
  border-bottom: 1px dashed #edf1f6;
}

.path-row:last-child {
  border-bottom: none;
}

.path-row span {
  width: 42px;
  flex: 0 0 auto;
  font-size: 11px;
  color: #98a2b3;
}

.path-row code {
  flex: 1;
  font-family: 'JetBrains Mono', Consolas, monospace;
  font-size: 11.5px;
  color: #475467;
  word-break: break-all;
}

.site-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.site-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 10px 12px;
  border: 1px solid var(--nsm-border);
  border-radius: 10px;
  background: #fbfcfe;
}

.site-item div {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.site-item strong {
  font-size: 12.5px;
  word-break: break-all;
}

.site-item span {
  font-size: 10.5px;
  color: #98a2b3;
  font-family: 'JetBrains Mono', Consolas, monospace;
  word-break: break-all;
}

.empty-inline {
  padding: 14px;
  border-radius: 10px;
  background: #f8fafc;
  color: #98a2b3;
  font-size: 12px;
  text-align: center;
}

.fingerprint {
  display: flex;
  flex-direction: column;
  gap: 5px;
  padding-top: 4px;
}

.fingerprint span {
  font-size: 11px;
  color: #98a2b3;
}

.fingerprint code {
  font-family: 'JetBrains Mono', Consolas, monospace;
  font-size: 10.5px;
  color: #667085;
  word-break: break-all;
}

@media (max-width: 720px) {
  .form-row,
  .switch-row {
    grid-template-columns: 1fr;
  }

  .detail-grid {
    grid-template-columns: 1fr;
  }
}
</style>
