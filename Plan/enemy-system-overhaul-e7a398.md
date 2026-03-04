# 敵人系統全面修正

修正四個根本問題：錯誤的 Physics 覆蓋、牆壁摩擦力卡牆、視線遮罩硬編碼導致穿牆射擊、生成間隔缺少可調欄位。

---

## 問題 1：我之前加的程式碼是錯的（先修這個）

`EnemyTickManager.Awake()` 中的 `Physics2D.IgnoreLayerCollision` 把你在 Project Settings 設好的 `Enemy ↔ Default = ON` 覆蓋掉了，讓敵人可以穿過牆壁。

**修法**：整個移除那段 IgnoreLayerCollision 程式碼，恢復你的正確設定。

---

## 問題 2：敵人卡牆根本原因（PhysicsMaterial2D 摩擦力）

就像你在地板上推箱子：箱子（敵人）跟牆壁之間有摩擦力，A* 說「往左走」但分幀 Tick 間隔期間，摩擦力把敵人速度磨成 0，下次 Tick 又把速度設回去，如此循環 → 看起來一動不動。

**錯誤的舊做法**：加 drag=0（只修了空氣阻力，沒修摩擦力）；改 IgnoreLayerCollision（移除了牆壁碰撞，打破了門機制）。

**正確修法（一行）**：靜態 `PhysicsMaterial2D`（friction=0, bounciness=0）套用到敵人 Collider。

- 只創建一次（static），不產生 GC
- 牆壁碰撞仍然 ON（敵人不穿牆）
- 敵人碰到牆角時會順滑沿牆滑動而非卡住
- 完全不需要改 Physics Matrix

```
// EnemyController.Awake() 中：
private static PhysicsMaterial2D s_zeroFriction;
if (s_zeroFriction == null) { s_zeroFriction = new(...) { friction=0, bounciness=0 }; }
GetComponent<Collider2D>().sharedMaterial = s_zeroFriction;
```

---

## 問題 3：遠程敵人透牆攻擊（遮罩硬編碼）

`HasLineOfSight()` 中 `_obstacleMask = LayerMask.GetMask("Default")` 是寫死的。如果你的牆壁不是在 "Default" Layer，這個永遠偵測不到牆 → 一直返回「視線暢通」→ 遠程敵人隔牆攻擊。

**修法**：把 `_obstacleMask` 改成 `[SerializeField] LayerMask _losObstacleMask`（必要設定欄位，非多餘選項）。用戶在 Inspector 勾選哪些 Layer 算障礙物，預設為 Default。

```
[SerializeField] private LayerMask _losObstacleMask = ~0; // 預設偵測所有 Layer
```

---

## 問題 4：波次和生成時間

**波次時間**（已有，無需改 Code）：
- `GameConfig.asset` → 「首波延遲」/「波次間隔」直接在 Inspector 改就好

**敵人生成間隔**（現在沒有）：
- 目前 `SpawnManager` 一口氣在 for 迴圈中生成所有敵人
- 加 `_spawnInterval`（秒）欄位 + Coroutine，讓敵人一隻一隻陸續出現
- 視覺上更流暢，CPU 尖峰更分散

---

## 修改清單

| 檔案 | 改動 |
|---|---|
| `EnemyTickManager.cs` | 移除錯誤的 IgnoreLayerCollision 程式碼 |
| `EnemyController.cs` | 加 static zero-friction PhysicsMaterial2D；`_losObstacleMask` 改為 SerializeField |
| `SpawnManager.cs` | 加 `_spawnInterval` 欄位 + 改用 Coroutine 生成 |

---

## 用中文解釋整個設計（12歲版）

就像玩具兵在棋盤上走路：

1. **A* 就是地圖**：它告訴玩具兵「你要從這條路走，繞過牆壁」
2. **摩擦力問題**：玩具兵的鞋底太黏，一碰到牆就被黏住了。修法是換一雙滑底鞋（friction=0），這樣碰到牆角時會自然滑過去
3. **透牆射擊**：你之前可能沒告訴電腦「什麼 Layer 算牆」，所以它以為玩家隨時都可以被看見。修法是在 Inspector 裡勾選你的牆壁 Layer
4. **之前的錯誤**：我讓玩具兵的腳直接穿過所有牆壁，結果它也穿過門了，整個遊戲機制就壞掉了。這次是真正修摩擦力，不是讓它穿牆
