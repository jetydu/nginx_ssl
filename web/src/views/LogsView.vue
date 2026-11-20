<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Refresh, Search } from '@element-plus/icons-vue'
import { logApi } from '../api'
import type { ActivityLog } from '../api/types'
import { errorMessage } from '../api/http'
import { categoryLabel, formatDate, levelType, relativeTime } from '../utils/format'

const logs = ref<ActivityLog[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const level = ref('all')
const category = ref('all')
const search = ref('')
const loading = ref(false)
const autoRefresh = ref(false)
let timer: number | undefined

const levelOptions = [
  { label: '全部级别', value: 'all' },
  { label: '信息', value: 'info' },
  { label: '成功', value: 'success' },
  { label: '警告', value: 'warning' },
  { label: '错误', value: 'error' },
]

const categoryOptions = [
  { label: '全部类型', value: 'all' },
  { label: '签发', value: 'issue' },
  { label: '续期', value: 'renew' },
  { label: '部署', value: 'deploy' },
  { label: '扫描', value: 'scan' },
  { label: 'Nginx', value: 'nginx' },
  { label: '导入', value: 'import' },
  { label: '认证', value: 'auth' },
  { label: '系统', value: 'system' },
]

const pageCount = computed(() => Math.max(1, Math.ceil(total.value / pageSize.value)))

async function load() {
  loading.value = true
  try {
    const result = await logApi.list({
      page: page.value,
      pageSize: pageSize.value,
      level: level.value === 'all' ? undefined : level.value,
      category: category.value === 'all' ? undefined : category.value,
      search: search.value.trim() || undefined,
    })
    logs.value = result.items
    total.value = result.total
    if (page.value > pageCount.value) {
      page.value = pageCount.value
      await load()
    }
  } catch (error) {
    ElMessage.error(errorMessage(error))
  } finally {
    loading.value = false
  }
}

function resetAndLoad() {
  page.value = 1
  load()
}

async function clearLogs() {
  try {
    await ElMessageBox.confirm('确定要清空全部操作日志吗？此操作不可恢复。', '清空日志', {
      confirmButtonText: '清空',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }

  try {
    await logApi.clear()
    ElMessage.success('日志已清空')
    page.value = 1
    await load()
  } catch (error) {
    ElMessage.error(errorMessage(error))
  }
}

function startTimer() {
  stopTimer()
  if (autoRefresh.value) {
    timer = window.setInterval(load, 15_000)
  }
}

function stopTimer() {
  if (timer) {
    window.clearInterval(timer)
    timer = undefined
  }
}

watch(autoRefresh, startTimer)

onMounted(load)
onBeforeUnmount(stopTimer)
</script>

<template>
  <div class="page">
    <div class="page-heading">
      <div>
        <h1>操作日志</h1>
        <p>记录证书申请、续期、部署、扫描与认证等关键操作</p>
      </div>
      <div class="heading-actions">
        <el-switch v-model="autoRefresh" active-text="自动刷新" />
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        <el-button :icon="Delete" @click="clearLogs">清空日志</el-button>
      </div>
    </div>

    <section class="panel">
      <div class="toolbar">
        <div class="filters">
          <el-select v-model="level" style="width: 130px" @change="resetAndLoad">
            <el-option v-for="item in levelOptions" :key="item.value" :label="item.label" :value="item.value" />
          </el-select>
          <el-select v-model="category" style="width: 130px" @change="resetAndLoad">
            <el-option v-for="item in categoryOptions" :key="item.value" :label="item.label" :value="item.value" />
          </el-select>
          <el-input
            v-model="search"
            placeholder="搜索消息或域名…"
            clearable
            style="width: 240px"
            :prefix-icon="Search"
            @keyup.enter="resetAndLoad"
            @clear="resetAndLoad"
          />
        </div>
        <span class="count-hint">共 {{ total }} 条记录</span>
      </div>

      <el-table v-loading="loading" :data="logs" row-key="id" empty-text="暂无日志">
        <el-table-column type="expand">
          <template #default="{ row }">
            <div class="log-detail">
              <div class="detail-row"><span>时间</span><b>{{ formatDate(row.timestamp) }}</b></div>
              <div class="detail-row"><span>域名</span><b>{{ row.domain || '—' }}</b></div>
              <div class="detail-row"><span>证书 ID</span><b>{{ row.certificateId ?? '—' }}</b></div>
              <div v-if="row.details" class="detail-pre-wrap">
                <span>详细信息</span>
                <pre>{{ row.details }}</pre>
              </div>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="时间" width="180">
          <template #default="{ row }">
            <div class="time-cell">
              <span>{{ formatDate(row.timestamp) }}</span>
              <span class="muted">{{ relativeTime(row.timestamp) }}</span>
            </div>
          </template>
        </el-table-column>

        <el-table-column label="级别" width="90">
          <template #default="{ row }">
            <el-tag :type="levelType(row.level)" size="small" effect="light">
              {{ row.level === 'success' ? '成功' : row.level === 'warning' ? '警告' : row.level === 'error' ? '错误' : '信息' }}
            </el-tag>
          </template>
        </el-table-column>

        <el-table-column label="类型" width="100">
          <template #default="{ row }">
            <el-tag size="small" effect="plain" type="info">{{ categoryLabel(row.category) }}</el-tag>
          </template>
        </el-table-column>

        <el-table-column label="域名" width="180">
          <template #default="{ row }">
            <span v-if="row.domain" class="domain mono">{{ row.domain }}</span>
            <span v-else class="muted">—</span>
          </template>
        </el-table-column>

        <el-table-column label="消息" min-width="300">
          <template #default="{ row }">
            <span class="log-message">{{ row.message }}</span>
          </template>
        </el-table-column>
      </el-table>

      <div class="pagination-wrap">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :total="total"
          :page-sizes="[20, 50, 100]"
          layout="total, sizes, prev, pager, next"
          background
          @current-change="load"
          @size-change="resetAndLoad"
        />
      </div>
    </section>
  </div>
</template>

<style scoped>
.heading-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
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

.time-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
  font-size: 12px;
}

.time-cell .muted {
  font-size: 11px;
}

.domain {
  font-size: 11.5px;
  color: var(--nsm-brand-dark);
  word-break: break-all;
}

.log-message {
  font-size: 12.5px;
  line-height: 1.55;
  color: #344054;
}

.log-detail {
  padding: 14px 28px 18px;
  background: #fbfcfe;
  border-top: 1px solid #f2f4f7;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.detail-row {
  display: flex;
  gap: 14px;
  font-size: 12px;
}

.detail-row span {
  width: 62px;
  color: #98a2b3;
  flex: 0 0 auto;
}

.detail-row b {
  font-weight: 500;
  color: #475467;
}

.detail-pre-wrap {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.detail-pre-wrap span {
  font-size: 11px;
  color: #98a2b3;
}

.detail-pre-wrap pre {
  margin: 0;
  padding: 12px 14px;
  border-radius: 8px;
  background: #0d1728;
  color: #d6e2f0;
  font-family: 'JetBrains Mono', Consolas, monospace;
  font-size: 11px;
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 260px;
  overflow: auto;
}

.pagination-wrap {
  padding: 14px 20px;
  border-top: 1px solid var(--nsm-border);
}
</style>
