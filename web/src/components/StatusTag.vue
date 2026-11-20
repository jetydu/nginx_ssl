<script setup lang="ts">
import { computed } from 'vue'
import { statusMeta } from '../utils/format'

const props = defineProps<{ status: string; size?: 'small' | 'default' | 'large' }>()
const meta = computed(() => statusMeta(props.status))
</script>

<template>
  <el-tag :type="meta.type" :size="props.size ?? 'small'" effect="light" class="status-tag">
    <span class="status-dot" :style="{ background: meta.color }" />
    {{ meta.label }}
  </el-tag>
</template>

<style scoped>
.status-tag {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  border-radius: 999px;
  padding: 0 10px;
}

.status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  display: inline-block;
}
</style>
