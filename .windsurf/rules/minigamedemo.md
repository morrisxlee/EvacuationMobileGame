---
trigger: always_on
---
**語言與框架**：
   - 所有腳本一律使用 C#（.cs 檔案）。
   - 優先使用 Unity 2022.3.62f3c1 的最新穩定 API
   - 嚴禁使用過時的 API（如舊版 Input、舊 Physics）
   - MonoBehaviour 腳本優先使用 [SerializeField] + private 欄位，而不是 public。
   - Always explain everything in detail using plain and easy-to-understand traditional Chinese. Use a colloquial, everyday manner of speaking as much as possible, avoiding abstruse words.

**vibe code 輸出風格**：
   - 腳本應隱藏編輯器中使用者不需要且可能在選取或禁用時造成問題的不必要選項，以避免手動誤改
   - 腳本應分析代碼當中會令功能有問題或嚴重影響效能的風險。
   - 自始至終都選擇最好的方法來做。使用者完全不介意工作量有多大，因為最重要的是優化和穩定。
   - 不要使用回退方法來防呆，而是給使用者一份錯誤日誌(With toggle)，這樣使用者就能立即知道問題並加以修復。
   - 腳本應考慮到使用者的遊戲將會同時出現超過 1000 個敵人，並應已經考慮到潛在的效能問題（所有內容包括敵人和特效全部都在使用物件池）。
