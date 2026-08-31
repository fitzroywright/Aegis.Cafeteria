# Aegis.Cafeteria v76

## Windows refund grid sizing

- Fixed the customer transaction/refund-history layout so the history row receives the remaining vertical space instead of being squeezed by an incorrect TableLayoutPanel row-style definition.
- The customer transaction/refund-history grid now reserves enough height for at least five complete transaction rows.
- The recent refundable-order chooser now reserves enough height for all five refundable orders without scrolling.
- The partial-refund item grid now reserves enough height for at least five complete item rows.
- Increased the refund dialog minimum height so its tender and action controls cannot collapse the refund-item grid below five rows.

No financial, authorization, refund-allocation, FFP publishing, void, or Escape-key behavior was changed.
