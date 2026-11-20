<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { ElMessage } from 'element-plus'
import {
  Bell,
  Connection,
  FolderOpened,
  InfoFilled,
  Key,
  Lock,
  RefreshRight,
  Setting,
} from '@element-plus/icons-vue'
import { authApi, settingsApi } from '../api'
import type { AppSettings } from '../api/types'
import { errorMessage } from '../api/http'
import { useAuthStore } from '../stores/auth'

const auth = useAuthStore()
const settings = ref<AppSettings | null>(null)
const loading = ref(true)
const saving = ref(false)
const changing = ref(false)

const acmePlaceholder = 'https://acme-v02.api.letsencrypt.org/directory'

const form = reactive({
  acmeDirectory: '',
  acmeStagingDirectory: '',
  defaultEmail: '',
  renewDaysBefore: 30,
  checkIntervalMinutes: 60,
  autoRenewEnabled: true,
  autoImport: true,
  nginxBinary: '/usr/sbin/nginx',
  nginxConfigPath: '/etc/nginx/nginx.conf',
  nginxPrefix: '/etc/nginx',
  nginxReloadCommand: '/usr/sbin/nginx -s reload',
  nginxTestEnabled: true,
  defaultWebRoot: '/var/www/letsencrypt',
  certificateRoot: '/etc/nginx/ssl/letsencrypt',
  challengeMode: 'auto',
})

const passwordForm = reactive({
  currentPassword: '',
  newPassword: '',
  confirmPassword: '',
})

async function load() {
  loading.value = true
  try {
    const data = await settingsApi.get()
    settings.value = data
    form.acmeDirectory = data.acmeDirectory
    form.acmeStagingDirectory = data.acmeStagingDirectory
    form.defaultEmail = data.defaultEmail
    form.renewDaysBefore = data.renewDaysBefore
    form.checkIntervalMinutes = data.checkIntervalMinutes
    form.autoRenewEnabled = data.autoRenewEnabled
    form.autoImport = data.autoImport
    form.nginxBinary = data.nginxBinary
    form.nginxConfigPath = data.nginxConfigPath
    form.nginxPrefix = data.nginxPrefix
    form.nginxReloadCommand = data.nginxReloadCommand
    form.nginxTestEnabled = data.nginxTestEnabled
    form.defaultWebRoot = data.defaultWebRoot
    form.certificateRoot = data.certificateRoot
    form.challengeMode = data.challengeMode
  } catch (error) {
    ElMessage.error(errorMessage(error))
  } finally {
    loading.value = false
  }
}

async function save() {
  if (form.renewDaysBefore < 1 || form.renewDaysBefore > 120) {
    ElMessage.warning('续期提前天数必须在 1-120 之间')
    return
  }
  if (form.checkIntervalMinutes < 5 || form.checkIntervalMinutes > 10080) {
    ElMessage.warning('检查间隔必须在 5-10080 分钟之间')
    return
  }
  if (!/^https:\/\//i.test(form.acmeDirectory)) {
    ElMessage.warning('ACME 目录必须是 HTTPS 地址')
    return
  }
  if (!form.certificateRoot.startsWith('/') || !form.defaultWebRoot.startsWith('/')) {
    ElMessage.warning('证书目录与 Webroot 必须是绝对路径')
    return
  }

  saving.value = true
  try {
    settings.value = await settingsApi.update({ ...form })
    ElMessage.success('设置已保存')
  } catch (error) {
    ElMessage.error({ message: errorMessage(error), duration: 8000, showClose: true })
  } finally {
    saving.value = false
  }
}

function useProduction() {
  form.acmeDirectory = settings.value?.letsEncryptProduction ?? 'https://acme-v02.api.letsencrypt.org/directory'
  form.acmeStagingDirectory = settings.value?.letsEncryptStaging ?? 'https://acme-staging-v02.api.letsencrypt.org/directory'
}

function useStaging() {
  form.acmeDirectory = settings.value?.letsEncryptStaging ?? 'https://acme-staging-v02.api.letsencrypt.org/directory'
}

async function changePassword() {
  if (!passwordForm.currentPassword || !passwordForm.newPassword) {
    ElMessage.warning('请填写当前密码和新密码')
    return
  }
  if (passwordForm.newPassword.length < 8) {
    ElMessage.warning('新密码至少需要 8 个字符')
    return
  }
  if (passwordForm.newPassword !== passwordForm.confirmPassword) {
    ElMessage.warning('两次输入的新密码不一致')
    return
  }

  changing.value = true
  try {
    await authApi.changePassword(passwordForm.currentPassword, passwordForm.newPassword)
    auth.markPasswordChanged()
    passwordForm.currentPassword = ''
    passwordForm.newPassword = ''
    passwordForm.confirmPassword = ''
    ElMessage.success('密码已修改')
    if (settings.value) settings.value.isDefaultPassword = false
  } catch (error) {
    ElMessage.error(errorMessage(error))
  } finally {
    changing.value = false
  }
}

onMounted(load)
</script>

<template>
  <div v-loading="loading" class="page">
    <div class="page-heading">
      <div>
        <h1>系统设置</h1>
        <p>配置 ACME、续期策略、nginx 路径与管理员安全选项</p>
      </div>
      <div class="heading-actions">
        <el-button :icon="RefreshRight" @click="load">重新读取</el-button>
        <el-button type="primary" :icon="Setting" :loading="saving" @click="save">保存设置</el-button>
      </div>
    </div>

    <el-alert v-if="settings?.isDefaultPassword" type="warning" :closable="false" show-icon class="password-alert">
      <template #title>当前仍在使用默认密码</template>
      默认密码为 <b>admin123</b>，请在下方“管理员安全”中立即修改，避免控制台被未授权访问。
    </el-alert>

    <div class="settings-grid">
      <section class="panel">
        <div class="panel-header">
          <div class="panel-title">
            <el-icon><Key /></el-icon>
            <span>ACME 与证书颁发</span>
          </div>
          <div class="quick-actions">
            <el-button text size="small" @click="useProduction">使用生产环境</el-button>
            <el-button text size="small" @click="useStaging">使用测试环境</el-button>
          </div>
        </div>
        <div class="panel-body">
          <el-form label-position="top">
            <el-form-item label="ACME 目录地址">
              <el-input v-model="form.acmeDirectory" :placeholder="acmePlaceholder" />
              <div class="hint">
                Let's Encrypt 生产环境会签发受浏览器信任的证书。测试环境仅用于调试，证书不受信任。
              </div>
            </el-form-item>
            <el-form-item label="Staging 目录地址">
              <el-input v-model="form.acmeStagingDirectory" />
            </el-form-item>
            <el-form-item label="默认联系邮箱">
              <el-input v-model="form.defaultEmail" placeholder="admin@example.com" />
              <div class="hint">创建证书时若未填写邮箱，将使用该地址注册 ACME 账户。</div>
            </el-form-item>
          </el-form>
        </div>
      </section>

      <section class="panel">
        <div class="panel-header">
          <div class="panel-title">
            <el-icon><Bell /></el-icon>
            <span>自动续期策略</span>
          </div>
          <el-tag :type="form.autoRenewEnabled ? 'success' : 'info'" size="small" effect="light">
            {{ form.autoRenewEnabled ? '已启用' : '已停用' }}
          </el-tag>
        </div>
        <div class="panel-body">
          <div class="switch-line">
            <div>
              <strong>启用自动续期</strong>
              <span>后台任务会定期检查证书并自动续签即将到期的证书</span>
            </div>
            <el-switch v-model="form.autoRenewEnabled" />
          </div>
          <div class="switch-line">
            <div>
              <strong>自动导入 Nginx 证书</strong>
              <span>扫描 nginx 配置，把尚未纳管的证书加入监控（不会自动续期第三方证书）</span>
            </div>
            <el-switch v-model="form.autoImport" />
          </div>
          <el-form label-position="top" class="inline-form">
            <el-form-item label="提前多少天续期">
              <el-input-number v-model="form.renewDaysBefore" :min="1" :max="120" controls-position="right" />
              <span class="unit">天</span>
            </el-form-item>
            <el-form-item label="检查间隔">
              <el-input-number v-model="form.checkIntervalMinutes" :min="5" :max="10080" controls-position="right" />
              <span class="unit">分钟</span>
            </el-form-item>
          </el-form>
          <div class="hint">默认每 60 分钟检查一次，在到期前 30 天开始续期。修改间隔后将在下一轮检查时生效。</div>
        </div>
      </section>

      <section class="panel">
        <div class="panel-header">
          <div class="panel-title">
            <el-icon><FolderOpened /></el-icon>
            <span>证书与验证路径</span>
          </div>
        </div>
        <div class="panel-body">
          <el-form label-position="top">
            <el-form-item label="证书默认存放目录">
              <el-input v-model="form.certificateRoot" placeholder="/etc/nginx/ssl/letsencrypt" />
              <div class="hint">新建证书时按 <code>&lt;目录&gt;/&lt;主域名&gt;/fullchain.pem</code> 生成路径。</div>
            </el-form-item>
            <el-form-item label="默认 Webroot 目录">
              <el-input v-model="form.defaultWebRoot" placeholder="/var/www/letsencrypt" />
              <div class="hint">HTTP-01 验证文件会写入 <code>&lt;webroot&gt;/.well-known/acme-challenge/</code>。</div>
            </el-form-item>
            <el-form-item label="HTTP-01 验证方式">
              <el-radio-group v-model="form.challengeMode" class="mode-group">
                <el-radio value="auto" border>
                  <span class="radio-title">自动注入验证路径</span>
                  <span class="radio-desc">临时修改匹配的 80 端口 server 块，验证后自动恢复</span>
                </el-radio>
                <el-radio value="webroot" border>
                  <span class="radio-title">仅使用固定 Webroot</span>
                  <span class="radio-desc">需要站点已配置 /.well-known 指向该目录</span>
                </el-radio>
              </el-radio-group>
            </el-form-item>
          </el-form>
        </div>
      </section>

      <section class="panel">
        <div class="panel-header">
          <div class="panel-title">
            <el-icon><Connection /></el-icon>
            <span>Nginx 集成</span>
          </div>
          <el-tag size="small" effect="plain" type="info">systemd / 二进制</el-tag>
        </div>
        <div class="panel-body">
          <el-form label-position="top">
            <div class="form-row">
              <el-form-item label="nginx 可执行文件">
                <el-input v-model="form.nginxBinary" placeholder="/usr/sbin/nginx" />
              </el-form-item>
              <el-form-item label="配置根目录（prefix）">
                <el-input v-model="form.nginxPrefix" placeholder="/etc/nginx" />
              </el-form-item>
            </div>
            <el-form-item label="主配置文件">
              <el-input v-model="form.nginxConfigPath" placeholder="/etc/nginx/nginx.conf" />
            </el-form-item>
            <el-form-item label="重载命令">
              <el-input v-model="form.nginxReloadCommand" placeholder="/usr/sbin/nginx -s reload" />
              <div class="hint">支持占位符 <code>{binary}</code> 与 <code>{config}</code>。使用 systemd 时可填 <code>systemctl reload nginx</code>。</div>
            </el-form-item>
            <div class="switch-line compact">
              <div>
                <strong>重载前检查配置</strong>
                <span>执行 nginx -t，未通过时取消重载</span>
              </div>
              <el-switch v-model="form.nginxTestEnabled" />
            </div>
          </el-form>
        </div>
      </section>

      <section class="panel">
        <div class="panel-header">
          <div class="panel-title">
            <el-icon><Lock /></el-icon>
            <span>管理员安全</span>
          </div>
        </div>
        <div class="panel-body">
          <el-form label-position="top" @submit.prevent="changePassword">
            <el-form-item label="管理员用户名">
              <el-input :model-value="settings?.adminUsername ?? 'admin'" disabled />
            </el-form-item>
            <el-form-item label="当前密码">
              <el-input v-model="passwordForm.currentPassword" type="password" show-password autocomplete="current-password" />
            </el-form-item>
            <div class="form-row">
              <el-form-item label="新密码">
                <el-input v-model="passwordForm.newPassword" type="password" show-password autocomplete="new-password" />
              </el-form-item>
              <el-form-item label="确认新密码">
                <el-input v-model="passwordForm.confirmPassword" type="password" show-password autocomplete="new-password" />
              </el-form-item>
            </div>
            <el-button type="primary" plain :loading="changing" @click="changePassword">修改密码</el-button>
            <div class="hint">密码至少 8 个字符。修改后建议重新登录。</div>
          </el-form>
        </div>
      </section>

      <section class="panel info-panel">
        <div class="panel-header">
          <div class="panel-title">
            <el-icon><InfoFilled /></el-icon>
            <span>运行说明</span>
          </div>
        </div>
        <div class="panel-body info-body">
          <div class="info-row">
            <span>运行环境</span>
            <b>.NET 10 · ASP.NET Core</b>
          </div>
          <div class="info-row">
            <span>ACME 协议</span>
            <b>RFC 8555 · HTTP-01</b>
          </div>
          <div class="info-row">
            <span>数据库</span>
            <b>SQLite（data 目录）</b>
          </div>
          <div class="info-row">
            <span>API 文档</span>
            <a href="/scalar" target="_blank" rel="noreferrer">打开 Scalar 文档</a>
          </div>
          <div class="info-note">
            申请证书要求域名已解析到本机，且公网可以访问 80 端口。若服务器位于 CDN 或反向代理之后，请确保 80 端口验证请求能够回源。
          </div>
        </div>
      </section>
    </div>
  </div>
</template>

<style scoped>
.heading-actions {
  display: flex;
  gap: 10px;
}

.password-alert {
  border-radius: 12px;
}

.settings-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20px;
  align-items: start;
}

.quick-actions {
  display: flex;
  gap: 4px;
}

.hint {
  width: 100%;
  margin-top: 6px;
  font-size: 11.5px;
  line-height: 1.6;
  color: #98a2b3;
}

.hint code {
  padding: 1px 4px;
  border-radius: 4px;
  background: #f2f4f7;
  font-family: 'JetBrains Mono', Consolas, monospace;
  font-size: 11px;
  color: #667085;
}

.switch-line {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 13px 0;
  border-bottom: 1px dashed #edf1f6;
}

.switch-line.compact {
  padding-bottom: 0;
  border-bottom: none;
}

.switch-line div {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.switch-line strong {
  font-size: 13px;
  font-weight: 600;
}

.switch-line span {
  font-size: 11.5px;
  color: var(--nsm-muted);
  line-height: 1.5;
}

.inline-form {
  display: flex;
  gap: 18px;
  margin-top: 16px;
}

.inline-form :deep(.el-form-item) {
  margin-bottom: 0;
}

.unit {
  margin-left: 8px;
  font-size: 12px;
  color: var(--nsm-muted);
}

.mode-group {
  display: flex;
  flex-direction: column;
  gap: 10px;
  width: 100%;
}

.mode-group :deep(.el-radio) {
  width: 100%;
  height: auto;
  margin-right: 0;
  padding: 12px 14px;
  align-items: flex-start;
}

.mode-group :deep(.el-radio__label) {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.radio-title {
  font-size: 12.5px;
  font-weight: 600;
  color: var(--nsm-text);
}

.radio-desc {
  font-size: 11px;
  color: #98a2b3;
  font-weight: 400;
  line-height: 1.5;
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 14px;
}

.info-body {
  display: flex;
  flex-direction: column;
}

.info-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  padding: 11px 0;
  border-bottom: 1px dashed #edf1f6;
  font-size: 12.5px;
}

.info-row span {
  color: var(--nsm-muted);
}

.info-row b {
  font-weight: 550;
  color: #344054;
}

.info-row a {
  color: var(--nsm-brand-dark);
  font-weight: 500;
}

.info-note {
  margin-top: 16px;
  padding: 12px 14px;
  border-radius: 10px;
  background: #f0f9f7;
  border: 1px solid #d5efeb;
  color: #567c77;
  font-size: 11.5px;
  line-height: 1.65;
}

@media (max-width: 1100px) {
  .settings-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 620px) {
  .form-row,
  .inline-form {
    grid-template-columns: 1fr;
    flex-direction: column;
  }
}
</style>
