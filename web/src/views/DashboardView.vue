<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage, ElMessageBox } from 'element-plus'
import type { EChartsOption } from 'echarts'
import {
  AlarmClock,
  ArrowRight,
  CircleCheckFilled,
  CircleCloseFilled,
  Collection,
  Connection,
  Document,
  InfoFilled,
  Refresh,
  RefreshRight,
  Tickets,
  WarningFilled,
} from '@element-plus/icons-vue'
import { dashboardApi, nginxApi } from '../api'
import type { Dashboard } from '../api/types'
import { errorMessage } from '../api/http'
import {
  certProgress,
  categoryLabel,
  daysLabel,
  formatDate,
  issuerName,
  relativeTime,
  statusMeta,
} from '../utils/format'
import StatusTag from '../components/StatusTag.vue'
import BaseChart from '../components/BaseChart.vue'

const router = useRouter()
const data = ref<Dashboard | null>(null)
const loading = ref(true)
const testing = ref(false)
const reloading = ref(false)

async function load() {
  loading.value = true
  try {
    data.value = await dashboardApi.get()
  } catch (error) {
    ElMessage.error(errorMessage(error))
  } finally {
    loading.value = false
  }
}

const statusOption = computed<EChartsOption>(() => {
  const buckets = (data.value?.statusBuckets ?? []).filter((item) => item.count > 0)
  return {
    tooltip: { trigger: 'item', formatter: '{b}: {c} 张 ({d}%)' },
    legend: {
      orient: 'vertical',
      right: 0,
      top: 'center',
      icon: 'circle',
      itemWidth: 8,
      itemHeight: 8,
      textStyle: { color: '#667085', fontSize: 12 },
      formatter: (name: string) => {
        const item = buckets.find((x) => statusMeta(x.status).label === name)
        return `${name}  ${item?.count ?? 0}`
      },
    },
    series: [
      {
        type: 'pie',
        radius: ['58%', '78%'],
        center: ['34%', '50%'],
        avoidLabelOverlap: true,
        itemStyle: { borderColor: '#fff', borderWidth: 3, borderRadius: 4 },
        label: { show: false },
        emphasis: { scale: true, scaleSize: 4 },
        data: buckets.map((item) => ({
          name: statusMeta(item.status).label,
          value: item.count,
          itemStyle: { color: statusMeta(item.status).color },
        })),
      },
    ],
    graphic: [
      {
        type: 'text',
        left: '27%',
        top: '44%',
        style: {
          text: String(data.value?.totalCertificates ?? 0),
          fontSize: 26,
          fontWeight: 700,
          fill: '#182230',
          textAlign: 'center',
        },
      },
      {
        type: 'text',
        left: '27%',
        top: '58%',
        style: {
          text: '证书总数',
          fontSize: 11,
          fill: '#98a2b3',
          textAlign: 'center',
        },
      },
    ],
  }
})

async function testConfig() {
  testing.value = true
  try {
    const result = await nginxApi.test()
    if (result.success) ElMessage.success('nginx 配置检查通过')
    else ElMessage.error({ message: result.output || 'nginx 配置检查失败', duration: 6000 })
    await load()
  } catch (error) {
    ElMessage.error(errorMessage(error))
  } finally {
    testing.value = false
  }
}

async function reloadNginx() {
  try {
    await ElMessageBox.confirm('重新加载 nginx 会平滑应用当前配置，期间服务不会中断。是否继续？', '重载 Nginx', {
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
    await load()
  } catch (error) {
    ElMessage.error(errorMessage(error))
  } finally {
    reloading.value = false
  }
}

function openCertificate() {
  router.push('/certificates')
}

onMounted(load)
</script>

<template>
  <div v-loading="loading" class="page">
    <div class="page-heading">
      <div>
        <h1>证书总览</h1>
        <p>实时掌握所有域名的证书状态、到期时间与自动续期情况</p>
      </div>
      <el-button :icon="Refresh" :loading="loading" @click="load">刷新数据</el-button>
    </div>

    <div class="stats-grid">
      <article class="stat-card">
        <div class="stat-icon teal"><el-icon><Collection /></el-icon></div>
        <div class="stat-copy">
          <span>证书总数</span>
          <strong>{{ data?.totalCertificates ?? 0 }}</strong>
          <small>{{ data?.validCertificates ?? 0 }} 张当前有效</small>
        </div>
        <span class="stat-corner" />
      </article>

      <article class="stat-card">
        <div class="stat-icon amber"><el-icon><AlarmClock /></el-icon></div>
        <div class="stat-copy">
          <span>即将到期</span>
          <strong>{{ data?.expiringSoon ?? 0 }}</strong>
          <small>30 天内需要续期</small>
        </div>
        <span class="stat-corner" />
      </article>

      <article class="stat-card">
        <div class="stat-icon blue"><el-icon><RefreshRight /></el-icon></div>
        <div class="stat-copy">
          <span>自动续期</span>
          <strong>{{ data?.autoRenewEnabled ?? 0 }}</strong>
          <small>近 30 天续期 {{ data?.renewedLast30Days ?? 0 }} 次</small>
        </div>
        <span class="stat-corner" />
      </article>

      <article class="stat-card" :class="{ 'has-alert': (data?.errorCertificates ?? 0) > 0 }">
        <div class="stat-icon red"><el-icon><CircleCloseFilled /></el-icon></div>
        <div class="stat-copy">
          <span>异常证书</span>
          <strong>{{ data?.errorCertificates ?? 0 }}</strong>
          <small>已过期 {{ data?.expiredCertificates ?? 0 }} 张</small>
        </div>
        <span class="stat-corner" />
      </article>
    </div>

    <div class="grid-2">
      <div class="left-column">
        <section class="panel">
          <div class="panel-header">
            <div class="panel-title">
              <el-icon><AlarmClock /></el-icon>
              <span>证书到期时间线</span>
            </div>
            <div class="expiry-hint">
              <template v-if="data?.nextExpiry">
                <span class="muted">最近到期</span>
                <b>{{ data.nextExpiryDomain }}</b>
                <span class="muted">{{ formatDate(data.nextExpiry, false) }}</span>
              </template>
              <el-button text type="primary" @click="openCertificate">
                全部证书<el-icon class="el-icon--right"><ArrowRight /></el-icon>
              </el-button>
            </div>
          </div>

          <div v-if="data?.upcoming?.length" class="timeline-list">
            <div v-for="cert in data.upcoming" :key="cert.id" class="timeline-row">
              <div class="timeline-main">
                <div class="timeline-domain">
                  <span class="domain-name">{{ cert.primaryDomain }}</span>
                  <StatusTag :status="cert.status" />
                  <el-tag v-if="cert.domains.length > 1" size="small" effect="plain" type="info">
                    +{{ cert.domains.length - 1 }} 域名
                  </el-tag>
                </div>
                <div class="timeline-meta">
                  <span>{{ issuerName(cert.issuer) }}</span>
                  <span class="dot-sep">·</span>
                  <span>{{ formatDate(cert.notAfter, false) }} 到期</span>
                </div>
                <el-progress
                  :percentage="certProgress(cert)"
                  :show-text="false"
                  :stroke-width="6"
                  :color="statusMeta(cert.status).color"
                  class="timeline-progress"
                />
              </div>
              <div class="timeline-side">
                <span class="days" :style="{ color: statusMeta(cert.status).color }">
                  {{ daysLabel(cert.daysRemaining) }}
                </span>
                <span class="nginx-link">
                  <el-icon><Connection /></el-icon>
                  {{ cert.nginxSiteCount }} 个站点
                </span>
              </div>
            </div>
          </div>

          <div v-else class="empty-state">
            <el-icon><Document /></el-icon>
            <strong>还没有证书记录</strong>
            <span>创建证书或从 nginx 配置中导入已有证书</span>
            <el-button type="primary" @click="openCertificate">去创建证书</el-button>
          </div>
        </section>

        <div class="bottom-grid">
          <section class="panel">
            <div class="panel-header">
              <div class="panel-title">
                <el-icon><Collection /></el-icon>
                <span>证书状态分布</span>
              </div>
            </div>
            <div class="panel-body chart-body">
              <BaseChart v-if="data?.totalCertificates" :option="statusOption" height="220px" />
              <div v-else class="empty-state small">
                <span>暂无数据</span>
              </div>
            </div>
          </section>

          <section class="panel">
            <div class="panel-header">
              <div class="panel-title">
                <el-icon><CircleCheckFilled /></el-icon>
                <span>运行概况</span>
              </div>
            </div>
            <div class="panel-body overview-body">
              <div class="overview-row">
                <span>Nginx server 块</span>
                <strong>{{ data?.nginxSites ?? 0 }}</strong>
              </div>
              <div class="overview-row">
                <span>启用 HTTPS 的站点</span>
                <strong>{{ data?.nginxSslSites ?? 0 }}</strong>
              </div>
              <div class="overview-row">
                <span>自动续期启用率</span>
                <strong>
                  {{ data?.totalCertificates
                    ? Math.round(((data.autoRenewEnabled ?? 0) / data.totalCertificates) * 100)
                    : 0 }}%
                </strong>
              </div>
              <div class="overview-row">
                <span>下次检查预计</span>
                <strong class="muted">后台每小时自动执行</strong>
              </div>
            </div>
          </section>
        </div>
      </div>

      <div class="right-column">
        <section class="panel nginx-panel">
          <div class="panel-header">
            <div class="panel-title">
              <el-icon><Connection /></el-icon>
              <span>Nginx 状态</span>
            </div>
            <span class="health-pill" :class="{ ok: data?.nginx.running && data?.nginx.configOk, bad: !data?.nginx.running || !data?.nginx.configOk }">
              <span class="pulse" />
              {{ data?.nginx.running && data?.nginx.configOk ? '运行正常' : '需要关注' }}
            </span>
          </div>
          <div class="panel-body">
            <div class="nginx-meta">
              <div><span>服务进程</span><b>{{ data?.nginx.running ? '运行中' : '未运行' }}</b></div>
              <div><span>配置检查</span><b :class="{ danger: !data?.nginx.configOk }">{{ data?.nginx.configOk ? '通过' : '失败' }}</b></div>
              <div><span>版本</span><b class="mono">{{ data?.nginx.version || '—' }}</b></div>
            </div>

            <div class="config-path mono">{{ data?.nginx.configPath || '—' }}</div>

            <el-alert
              v-if="data?.nginx.message && !data?.nginx.configOk"
              type="error"
              :closable="false"
              show-icon
              class="nginx-error"
            >
              <template #title>配置检查未通过</template>
              <pre class="alert-pre">{{ data.nginx.message }}</pre>
            </el-alert>

            <div class="nginx-actions">
              <el-button :icon="CircleCheckFilled" :loading="testing" @click="testConfig">检查配置</el-button>
              <el-button type="primary" :icon="RefreshRight" :loading="reloading" @click="reloadNginx">重载 Nginx</el-button>
            </div>
          </div>
        </section>

        <section class="panel">
          <div class="panel-header">
            <div class="panel-title">
              <el-icon><Tickets /></el-icon>
              <span>最近活动</span>
            </div>
            <el-button text type="primary" @click="router.push('/logs')">查看全部</el-button>
          </div>
          <div v-if="data?.recentActivity?.length" class="activity-list">
            <div v-for="item in data.recentActivity" :key="item.id" class="activity-item">
              <div class="activity-icon" :class="item.level">
                <el-icon>
                  <CircleCheckFilled v-if="item.level === 'success'" />
                  <WarningFilled v-else-if="item.level === 'warning' || item.level === 'error'" />
                  <InfoFilled v-else />
                </el-icon>
              </div>
              <div class="activity-content">
                <div class="activity-top">
                  <el-tag size="small" effect="plain" type="info">{{ categoryLabel(item.category) }}</el-tag>
                  <span class="activity-time">{{ relativeTime(item.timestamp) }}</span>
                </div>
                <p>{{ item.message }}</p>
                <span v-if="item.domain" class="activity-domain">{{ item.domain }}</span>
              </div>
            </div>
          </div>
          <div v-else class="empty-state small">
            <span>暂无操作记录</span>
          </div>
        </section>
      </div>
    </div>
  </div>
</template>

<style scoped>
.stats-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 16px;
}

.stat-card {
  position: relative;
  overflow: hidden;
  display: flex;
  align-items: flex-start;
  gap: 14px;
  padding: 20px;
  border-radius: 16px;
  border: 1px solid var(--nsm-border);
  background: #fff;
  box-shadow: 0 6px 20px rgba(16, 24, 40, 0.045);
}

.stat-card.has-alert {
  border-color: #f3c7c4;
  background: linear-gradient(180deg, #fff 0%, #fff8f7 100%);
}

.stat-icon {
  width: 42px;
  height: 42px;
  border-radius: 12px;
  display: grid;
  place-items: center;
  font-size: 19px;
  flex: 0 0 auto;
}

.stat-icon.teal {
  color: #0b7f73;
  background: #e3f6f2;
}

.stat-icon.amber {
  color: #b26a00;
  background: #fff3df;
}

.stat-icon.blue {
  color: #2f6fed;
  background: #eaf1ff;
}

.stat-icon.red {
  color: #c44536;
  background: #fdeceb;
}

.stat-copy {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.stat-copy span {
  font-size: 12px;
  color: var(--nsm-muted);
}

.stat-copy strong {
  font-size: 27px;
  line-height: 1.15;
  letter-spacing: -0.03em;
  font-weight: 700;
}

.stat-copy small {
  font-size: 11.5px;
  color: #98a2b3;
  margin-top: 2px;
}

.stat-corner {
  position: absolute;
  width: 88px;
  height: 88px;
  right: -34px;
  top: -34px;
  border-radius: 50%;
  background: rgba(15, 159, 143, 0.045);
}

.left-column,
.right-column {
  display: flex;
  flex-direction: column;
  gap: 20px;
  min-width: 0;
}

.expiry-hint {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 12px;
}

.expiry-hint b {
  color: var(--nsm-text);
  font-weight: 600;
}

.timeline-list {
  padding: 6px 20px 10px;
}

.timeline-row {
  display: flex;
  align-items: center;
  gap: 20px;
  padding: 15px 0;
  border-bottom: 1px dashed #edf1f6;
}

.timeline-row:last-child {
  border-bottom: none;
}

.timeline-main {
  flex: 1;
  min-width: 0;
}

.timeline-domain {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.domain-name {
  font-size: 14px;
  font-weight: 600;
  color: var(--nsm-text);
}

.timeline-meta {
  display: flex;
  gap: 6px;
  color: var(--nsm-muted);
  font-size: 12px;
  margin: 5px 0 8px;
}

.dot-sep {
  color: #d0d5dd;
}

.timeline-progress {
  max-width: 460px;
}

.timeline-side {
  width: 118px;
  flex: 0 0 auto;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 5px;
}

.timeline-side .days {
  font-size: 12.5px;
  font-weight: 650;
}

.nginx-link {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 11px;
  color: #98a2b3;
}

.bottom-grid {
  display: grid;
  grid-template-columns: minmax(0, 1.25fr) minmax(240px, 0.75fr);
  gap: 20px;
}

.chart-body {
  padding: 10px 14px;
}

.overview-body {
  display: flex;
  flex-direction: column;
}

.overview-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 13px 0;
  border-bottom: 1px dashed #edf1f6;
  font-size: 12.5px;
}

.overview-row:last-child {
  border-bottom: none;
}

.overview-row span {
  color: var(--nsm-muted);
}

.overview-row strong {
  font-size: 14px;
  font-weight: 650;
}

.overview-row strong.muted {
  font-size: 12px;
  font-weight: 500;
}

.nginx-meta {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-bottom: 14px;
}

.nginx-meta > div {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 12.5px;
}

.nginx-meta span {
  color: var(--nsm-muted);
}

.nginx-meta b.danger {
  color: #d9534f;
}

.health-pill {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  padding: 4px 9px;
  border-radius: 999px;
  font-size: 11px;
  background: #fef3f2;
  color: #b42318;
}

.health-pill.ok {
  background: #ecfdf3;
  color: #027a48;
}

.health-pill .pulse {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
}

.config-path {
  padding: 9px 11px;
  border-radius: 8px;
  background: #f8fafc;
  border: 1px solid #edf1f6;
  color: #667085;
  word-break: break-all;
  margin-bottom: 12px;
}

.nginx-error {
  margin-bottom: 14px;
}

.alert-pre {
  margin: 6px 0 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
  font-size: 11.5px;
  line-height: 1.55;
}

.nginx-actions {
  display: flex;
  gap: 10px;
}

.nginx-actions .el-button {
  flex: 1;
}

.activity-list {
  padding: 8px 20px 14px;
}

.activity-item {
  display: flex;
  gap: 12px;
  padding: 12px 0;
  border-bottom: 1px dashed #edf1f6;
}

.activity-item:last-child {
  border-bottom: none;
}

.activity-icon {
  width: 28px;
  height: 28px;
  border-radius: 8px;
  display: grid;
  place-items: center;
  flex: 0 0 auto;
  color: #667085;
  background: #f2f4f7;
}

.activity-icon.success {
  color: #027a48;
  background: #ecfdf3;
}

.activity-icon.warning {
  color: #b54708;
  background: #fffaeb;
}

.activity-icon.error {
  color: #b42318;
  background: #fef3f2;
}

.activity-content {
  min-width: 0;
  flex: 1;
}

.activity-top {
  display: flex;
  align-items: center;
  gap: 8px;
}

.activity-time {
  font-size: 11px;
  color: #98a2b3;
}

.activity-content p {
  margin: 5px 0 0;
  font-size: 12.5px;
  line-height: 1.55;
  color: #344054;
  word-break: break-word;
}

.activity-domain {
  display: inline-block;
  margin-top: 3px;
  font-size: 11px;
  color: var(--nsm-brand-dark);
  font-family: 'JetBrains Mono', Consolas, monospace;
}

.empty-state.small {
  padding: 26px 16px;
}

@media (max-width: 1200px) {
  .stats-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 1100px) {
  .bottom-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 680px) {
  .stats-grid {
    grid-template-columns: 1fr;
  }

  .timeline-row {
    align-items: flex-start;
  }

  .timeline-side {
    width: auto;
    align-items: flex-end;
  }

  .expiry-hint .muted {
    display: none;
  }
}
</style>
